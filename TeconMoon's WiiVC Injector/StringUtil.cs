using System;
using System.Globalization;
using System.IO;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace TeconMoon_s_WiiVC_Injector
{
    public static class StringUtil
    {
        public static string RemoveSpecialChars(string v)
        {
            if (string.IsNullOrEmpty(v))
                return v;

            string s = RemoveDiacritics(v);
            return new string(s.Where(c => c < 128).ToArray());
        }

        public static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder(capacity: normalizedString.Length);

            for (int i = 0; i < normalizedString.Length; i++)
            {
                char c = normalizedString[i];
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder
                .ToString()
                .Normalize(NormalizationForm.FormC);
        }

        public static string ReplaceAt(this string input, int index, char newChar)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            char[] chars = input.ToCharArray();
            chars[index] = newChar;
            return new string(chars);
        }

        public static void CopyDirectory(string sourceDir, string destinationDir)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists) return;

            Directory.CreateDirectory(destinationDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                file.CopyTo(Path.Combine(destinationDir, file.Name), true);
            }

            foreach (DirectoryInfo subDir in dir.GetDirectories())
            {
                CopyDirectory(subDir.FullName, Path.Combine(destinationDir, subDir.Name));
            }
        }

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

