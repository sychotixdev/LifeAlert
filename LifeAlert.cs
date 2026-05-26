using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore2;
using ExileCore2.PoEMemory.Components;
using ExileCore2.Shared.Enums;

namespace LifeAlert;

public class LifeAlert : BaseSettingsPlugin<LifeAlertSettings>
{
    // Tracks the currently-running stun-response task so we never spawn two at once.
    private Task _stunHandlerTask;

    // Cancellation source is replaced on every area change so an in-flight task
    // from the previous area cannot interfere with the new one.
    private CancellationTokenSource _cts = new();

    private bool IsHandlerRunning => _stunHandlerTask is { IsCompleted: false };

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    public override bool Initialise()
    {
        return true;
    }

    public override void AreaChange(AreaInstance area)
    {
        // Cancel any in-progress sequence when we change areas (e.g. after a
        // successful logout/re-entry) and reset state for the new area.
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        _stunHandlerTask = null;
    }

    // -------------------------------------------------------------------------
    // Render — heavy-stun detection runs here per the design brief.
    // -------------------------------------------------------------------------

    public override void Render()
    {
        if (!Settings.Enable) return;
        if (!GameController.InGame && !GameController.IsLoading) return;
        if (Settings.DisableInSekhemas && GameController.Area.CurrentArea.Name == "Trial of the Sekhemas") return;
        if (IsHandlerRunning) return;

        try
        {
            var player = GameController.IngameState.Data.LocalPlayer;
            if (player == null) return;

            var stats = player.GetComponent<Stats>();
            if (stats == null) return;

            // StatDictionary maps GameStat -> int; TryGetValue returns true when the
            // key exists and writes the integer value into `stunValue`.
            if (stats.StatDictionary.TryGetValue(GameStat.IsHeavyStunned, out var stunValue) &&
                stunValue == 1)
            {
                LogMessage("[LifeAlert] Don't worry sir or madam, help is on the way!");
                _stunHandlerTask = Task.Run(() => HandleHeavyStunAsync(_cts.Token));
            }
        }
        catch (Exception ex)
        {
            LogError($"[LifeAlert] Error checking stun state: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Stun-response sequence
    // -------------------------------------------------------------------------

    private async Task HandleHeavyStunAsync(CancellationToken ct)
    {
        try
        {
            int maxAttempts = 10;
            // ── Step 1: Press Escape ─────────────────────────────────────────
            if (!GameController.Game.EscapeState.IsActive)
            {
                do
                {
                    maxAttempts--;
                    if (maxAttempts % 2 == 0)
                    {
                        Input.KeyPressRelease(Keys.Escape);
                    }
                    await Task.Delay(50, ct);
                } while (maxAttempts >= 0 && !GameController.Game.EscapeState.IsActive);
            }

            // ── Step 2: Wait up to EscapeMenuWaitMs for the escape menu ─────
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < Settings.EscapeMenuWaitMs.Value)
            {
                ct.ThrowIfCancellationRequested();

                if (GameController.Game.EscapeState.IsActive)
                    break;

                await Task.Delay(50, ct);
            }

            if (!GameController.Game.EscapeState.IsActive)
            {
                LogError("[LifeAlert] Escape menu did not open within the configured timeout. " +
                         "Aborting. (Consider raising 'Escape Menu Wait Ms' in settings.)");
                return;
            }

            // ── Step 3: Move cursor to the target element and left-click ────
            // Child path 0 → 0 → 0 → 8 leads to the character-select / logout button.
            var element = GameController.Game.EscapeState.HoveredElement
                .GetChildFromIndices(0, 0, 0, 8);

            if (element == null)
            {
                LogError("[LifeAlert] Could not resolve UI element at child indices [0,0,0,8]. " +
                         "The escape menu layout may have changed.");
                return;
            }

            ct.ThrowIfCancellationRequested();

            var windowTopLeft = GameController.Window.GetWindowRectangle().TopLeft;

            maxAttempts = 10;
            do
            {
                maxAttempts--;

                Input.SetCursorPos(windowTopLeft + element.GetClientRect().Center);

                // Lets break out before the sleep just to ensure the mouse doesn't move.
                if (element.HasShinyHighlight)
                    break;

                // Brief settle delay so the game registers the cursor position before the click.
                await Task.Delay(30, ct);
            } while (!element.HasShinyHighlight || maxAttempts >= 0);

            Input.Click(MouseButtons.Left);

            // ── Step 3.5: Wait to finish loading or to see the screen ─────────────────
            sw.Restart();
            while (sw.ElapsedMilliseconds < 10000)
            {
                ct.ThrowIfCancellationRequested();

                if (!GameController.Game.IsLoading || GameController.Game.IsSelectCharacterState)
                    break;

                await Task.Delay(100, ct);
            }

            // ── Step 4: Wait for the character-select screen ─────────────────
            sw.Restart();
            while (sw.ElapsedMilliseconds < Settings.CharacterSelectWaitMs.Value)
            {
                ct.ThrowIfCancellationRequested();

                if (GameController.Game.IsSelectCharacterState)
                    break;

                await Task.Delay(100, ct);
            }

            // Not a hard failure — we still attempt the Enter press even if we
            // timed out, because the screen might appear a moment later.
            if (!GameController.Game.IsSelectCharacterState)
            {
                LogMessage("[LifeAlert] Character-select screen was not detected within the " +
                           "configured timeout. Proceeding with Enter anyway.");
            }

            // ── Step 5: Post-character-select wait ───────────────────────────
            await Task.Delay(Settings.AfterCharSelectDelayMs.Value, ct);

            // ── Step 6: Press Enter to select the character ──────────────────
            ct.ThrowIfCancellationRequested();
            Input.KeyPressRelease(Keys.Enter);
        }
        catch (OperationCanceledException)
        {
            // Sequence was cancelled by an area change — this is expected; stay quiet.
        }
        catch (Exception ex)
        {
            LogError($"[LifeAlert] Unexpected error in stun-handler sequence: {ex.Message}");
        }
    }
}
