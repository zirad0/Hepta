using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Hepta
{
    public class Preferences
    {
        public OutputOptions Output = new OutputOptions();
        public MediaOptions Media = new MediaOptions();
        public int FontSize = 15, IconSize = 22, OpacityPercent = 100;
        public bool Hover = true;
        public string AccentColor = "#d7b36f", PetalColor = "#2c2c2e";
        public int Concurrency = 2, SuccessSeconds = 5, ErrorSeconds = 10;
        public bool ShowProgress = true;
        public int Schema = 2, MenuSize = 360, Duration = 250, Quality = 90, ConvertModifiers = 3, EditModifiers = 3, ConvertKey = 67, EditKey = 69;
        public bool English = false, Notify = true, Welcome = true, Startup = true, Center = false, History = true;
        public string Theme = "dark";
        public Dictionary<string, string[]> Pins = new Dictionary<string, string[]>();
        public Dictionary<string, string[]> Profiles = new Dictionary<string, string[]>();
        internal static string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hepta", "Data");
        public static string Location
        {
            get
            {
                return Path.Combine(DirectoryPath, "settings.json");
            }
        }

        public static Preferences Read(string path)
        {
            if (new FileInfo(path).Length > 1024 * 1024)
                throw new InvalidDataException("Settings size");
            var p = new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(path));
            if (p != null && p.Schema == 1)
                p.Schema = 2;
            if (p == null || p.Schema != 2 || !new[]
            {
                "light",
                "dark",
                "system"
            }.Contains(p.Theme) || p.Output == null || !p.Output.Valid() || p.Media == null || !p.Media.Valid() || p.FontSize < 10 || p.FontSize > 22 || p.IconSize < 12 || p.IconSize > 32 || p.OpacityPercent < 40 || p.OpacityPercent > 100 || !System.Text.RegularExpressions.Regex.IsMatch(p.AccentColor ?? "", "^#[0-9a-fA-F]{6}$") || !System.Text.RegularExpressions.Regex.IsMatch(p.PetalColor ?? "", "^#[0-9a-fA-F]{6}$") || p.Concurrency < 1 || p.Concurrency > 8 || p.SuccessSeconds < 1 || p.SuccessSeconds > 60 || p.ErrorSeconds < 1 || p.ErrorSeconds > 60 || p.Pins == null || p.Profiles == null || p.MenuSize < 240 || p.MenuSize > 680 || p.Duration < 0 || p.Duration > 1000 || p.Quality < 1 || p.Quality > 100 || (p.EditKey == p.ConvertKey && p.EditModifiers == p.ConvertModifiers) || !Chord.Valid(p.EditKey, p.EditModifiers) || !Chord.Valid(p.ConvertKey, p.ConvertModifiers))
                throw new InvalidDataException("Settings");
            foreach (var list in System.Linq.Enumerable.Concat(p.Profiles.Values, p.Pins.Values))
                if (list == null || list.Length != list.Distinct().Count() || Array.Exists(list, id => Array.IndexOf(Engine.Targets, id) < 0))
                    throw new InvalidDataException("Profile");
            return p;
        }

        public static void Write(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + "." + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(tmp, new JavaScriptSerializer().Serialize(value), new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Replace(tmp, path, null);
                else
                    File.Move(tmp, path);
            }
            finally
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
        }
    }
}
