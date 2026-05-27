using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore2;
using ExileCore2.PoEMemory.Components;
using ExileCore2.Shared.Enums;

namespace LifeAlert;

public class LifeAlert : BaseSettingsPlugin<LifeAlertSettings>
{
    private Task _stunResponseTask;
    private CancellationTokenSource _areaCancellation = new();

    private bool IsStunResponseRunning => _stunResponseTask is { IsCompleted: false };

    public override bool Initialise()
    {
        return true;
    }

    public override void AreaChange(AreaInstance area)
    {
        _areaCancellation.Cancel();
        _areaCancellation = new CancellationTokenSource();
        _stunResponseTask = null;
    }

    public override void Render()
    {
        if (!Settings.Enable) return;
        if (!GameController.InGame && !GameController.IsLoading) return;
        if (Settings.DisableInSekhemas && GameController.Area.CurrentArea.Name == "Trial of the Sekhemas") return;
        if (IsStunResponseRunning) return;

        try
        {
            var player = GameController.IngameState.Data.LocalPlayer;
            if (player == null) return;

            var stats = player.GetComponent<Stats>();
            if (stats == null) return;

            if (stats.StatDictionary.TryGetValue(GameStat.IsHeavyStunned, out var heavyStunned) &&
                heavyStunned == 1)
            {
                LogMessage("[LifeAlert] Don't worry sir or madam, help is on the way!");
                _stunResponseTask = Task.Run(() => HandleHeavyStunAsync(_areaCancellation.Token));
            }
        }
        catch (Exception ex)
        {
            LogError($"[LifeAlert] Error checking stun state: {ex.Message}");
        }
    }

    private async Task HandleHeavyStunAsync(CancellationToken cancellationToken)
    {
        try
        {
            int escapeAttemptsRemaining = 10;
            while (!GameController.Game.EscapeState.IsActive && escapeAttemptsRemaining-- > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Input.KeyPressRelease(Keys.Escape);
                await Task.Delay(250, cancellationToken);
            }

            if (!GameController.Game.EscapeState.IsActive)
            {
                LogError("[LifeAlert] Escape menu did not open after repeated attempts. Aborting.");
                return;
            }

            var logoutButton = GameController.Game.EscapeState.HoveredElement
                .GetChildFromIndices(0, 0, 0, 8);

            if (logoutButton == null)
            {
                LogError("[LifeAlert] Could not resolve the logout button. The escape menu layout may have changed.");
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var windowTopLeft = GameController.Window.GetWindowRectangle().TopLeft;

            int hoverAttemptsRemaining = 10;
            do
            {
                hoverAttemptsRemaining--;
                Input.SetCursorPos(windowTopLeft + logoutButton.GetClientRect().Center);

                if (logoutButton.HasShinyHighlight)
                    break;

                await Task.Delay(30, cancellationToken);
            } while (!logoutButton.HasShinyHighlight && hoverAttemptsRemaining >= 0);

            Input.Click(MouseButtons.Left);

            await WaitUntilAsync(() => GameController.Game.IsSelectCharacterState, cancellationToken);

            int loginAttemptsRemaining = 10;
            while (GameController.Game.IsSelectCharacterState && loginAttemptsRemaining-- > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Input.KeyPressRelease(Keys.Enter);
                await Task.Delay(Random.Shared.Next(50, 151), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LogError($"[LifeAlert] Unexpected error in stun-handler sequence: {ex.Message}");
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken, int pollIntervalMs = 100)
    {
        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(pollIntervalMs, cancellationToken);
        }
    }
}
