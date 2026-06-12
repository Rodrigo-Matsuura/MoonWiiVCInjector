using System.Threading.Tasks;
using Avalonia.Controls;

namespace Moon_WiiVC_Injector
{
    public static class GuiUtil
    {
        public static async Task<string> PromptInputAsync(Window parent, string text, string title, string defaultValue = "")
        {
            var prompt = new PromptWindow(text, title, defaultValue);
            var isOk = await prompt.ShowDialog<bool>(parent);
            return isOk ? prompt.Result : string.Empty;
        }
    }
}
