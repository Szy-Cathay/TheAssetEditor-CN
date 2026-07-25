using System.Text.Json;
using System.Windows;
using Shared.Core.ErrorHandling;

namespace Shared.Core.Services
{
    public class LocalizationManager
    {
        private readonly ILogger _logger = Logging.Create<LocalizationManager>();

        private const string LanguageFile = "Language_Cn.json";
        private static readonly string LanguageFilePath = Path.Combine(AppContext.BaseDirectory, LanguageFile);
        private Dictionary<string, string> _strings = [];

        public static LocalizationManager Instance { get; private set; }

        public LocalizationManager()
        {
            Instance = this;
        }

        public void LoadLanguage()
        {
            if (File.Exists(LanguageFilePath) == false)
            {
                MessageBox.Show($"找不到中文语言文件“{LanguageFile}”。");
                _logger.Here().Error($"Chinese language file was not found at {LanguageFilePath}");
                return;
            }

            try
            {
                var json = File.ReadAllText(LanguageFilePath);
                var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (strings == null || strings.Count == 0)
                {
                    MessageBox.Show($"中文语言文件解析失败：{LanguageFile}");
                    _logger.Here().Error($"Failed to parse Chinese language file {LanguageFilePath}");

                    _strings = [];
                    return;
                }

                _strings = strings;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"中文语言文件加载失败：{ex.Message}");
                _logger.Here().Error($"Failed to load Chinese language file {LanguageFilePath}: {ex.Message}");
            }
        }

        public string Get(string key)
        {
            if (_strings.TryGetValue(key, out var value))
                return value;

            _logger.Here().Error($"Failed to load localization key {key} from {LanguageFilePath}");
            return key;
        }

        public string GetFormat(string key, params object[] args)
        {
            try
            {
                return string.Format(Get(key), args);
            }
            catch (FormatException)
            {
                _logger.Here().Error($"Format error for localization key {key} in {LanguageFilePath}");
                return Get(key);
            }
        }
    }
}
