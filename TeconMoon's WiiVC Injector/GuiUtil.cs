using System;
using System.Windows.Forms;

namespace TeconMoon_s_WiiVC_Injector
{
    public static class GuiUtil
    {
        public static string PromptInput(string text, string title, string defaultValue = "")
        {
            using (Form prompt = new Form())
            {
                prompt.Width = 320;
                prompt.Height = 160;
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                prompt.Text = title;
                prompt.StartPosition = FormStartPosition.CenterParent;
                prompt.MaximizeBox = false;
                prompt.MinimizeBox = false;

                Label textLabel = new Label() { Left = 20, Top = 20, Text = text, Width = 280, AutoSize = true };
                TextBox textBox = new TextBox() { Left = 20, Top = 50, Text = defaultValue, Width = 260, MaxLength = 4 };
                Button confirmation = new Button() { Text = "OK", Left = 180, Width = 100, Top = 85, DialogResult = DialogResult.OK };

                prompt.Controls.Add(textLabel);
                prompt.Controls.Add(textBox);
                prompt.Controls.Add(confirmation);
                prompt.AcceptButton = confirmation;

                return prompt.ShowDialog() == DialogResult.OK ? textBox.Text : string.Empty;
            }
        }
    }
}
