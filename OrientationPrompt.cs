namespace Orbiter
{
    /// <summary>
    /// Wraps a single ScreenPrompt bound to the rebindable "Fix Orientation"
    /// command, so it renders with the same boxed-key-icon styling as every other
    /// cockpit prompt. Mirrors OrbitPrompt's shape; kept separate since this
    /// prompt's active/idle text is static (no per-target name to interpolate).
    /// </summary>
    public class OrientationPrompt
    {
        private ScreenPrompt _prompt;
        private bool _showingActive;
        private bool _registered;

        private const string IdleText = "<CMD>   Fix Orientation";
        private const string ActiveText = "<CMD>   Orientation Locked";

        public bool IsRegistered => _registered;

        public bool Setup(InputConsts.InputCommandType commandType)
        {
            if (_registered) return true;

            var command = InputLibrary.GetInputCommand(commandType);
            if (command == null) return false;

            var manager = Locator.GetPromptManager();
            if (manager == null) return false;

            _prompt = new ScreenPrompt(command, IdleText, 0, ScreenPrompt.DisplayState.Normal, false);
            // See OrbitPrompt: UpperLeft is where cockpit-context prompts actually render.
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

        public void SetActive(bool active)
        {
            if (!_registered || _showingActive == active) return;
            _prompt.SetText(active ? ActiveText : IdleText);
            _showingActive = active;
        }
    }
}
