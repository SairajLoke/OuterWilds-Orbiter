namespace Orbiter
{
    /// <summary>
    /// Wraps a single ScreenPrompt bound to the rebindable "Orbiter" command,
    /// so it renders with the same boxed-key-icon styling as every other cockpit
    /// prompt (Match Velocity, Roll, Free Look, ...).
    ///
    /// Setup() is safe to call every frame: PromptManager isn't guaranteed to exist
    /// on any particular frame after a scene load, so callers retry Setup() until it
    /// returns true instead of registering once and possibly failing silently.
    /// </summary>
    public class OrbitPrompt
    {
        private ScreenPrompt _prompt;
        private bool _showingActive;
        private bool _registered;

        private const string IdleText = "<CMD>   Orbiter";

        public bool IsRegistered => _registered;

        public bool Setup(InputConsts.InputCommandType commandType)
        {
            if (_registered) return true;

            var command = InputLibrary.GetInputCommand(commandType);
            if (command == null) return false;

            var manager = Locator.GetPromptManager();
            if (manager == null) return false;

            _prompt = new ScreenPrompt(command, IdleText, 0, ScreenPrompt.DisplayState.Normal, false);
            // UpperLeft, not UpperRight: that's where ShipPromptController puts every
            // other cockpit-context prompt (Match Velocity, Autopilot, Roll, Free
            // Look) - UpperRight isn't rendered while flying the ship.
            manager.AddScreenPrompt(_prompt, PromptPosition.UpperLeft, false);
            _registered = true;
            return true;
        }

        public void Teardown()
        {
            if (!_registered) return;
            Locator.GetPromptManager()?.RemoveScreenPrompt(_prompt);
            _prompt = null;
            _registered = false;
            _showingActive = false;
        }

        public void SetVisible(bool visible)
        {
            if (!_registered) return;
            _prompt.SetVisibility(visible);
        }

        public void SetIdleState()
        {
            if (!_registered || !_showingActive) return;
            _prompt.SetText(IdleText);
            _showingActive = false;
        }

        public void SetActiveState(string targetName)
        {
            if (!_registered || _showingActive) return;
            _prompt.SetText($"<CMD>   Orbiting {targetName}");
            _showingActive = true;
        }
    }
}
