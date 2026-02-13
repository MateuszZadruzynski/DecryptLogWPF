using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;

namespace Deszyfrowanie_Logów
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<string> LogMessages { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<string> AllMessages { get; set; } = new ObservableCollection<string>();
        public ICollectionView FilteredMessages { get; private set; }

        private string decryptedLog = string.Empty;
        private string selectedFilePath;

        private static readonly Regex LoggerRegex = new Regex(@"\[\w+Logger\]\s\d{2}:\d{2}:\d{2}\.\d{2}\s\d{5}\s[RW]\s.*", RegexOptions.Compiled);
        private static readonly Regex ApduRegex = new Regex(@"<- APDU:.*|-> APDU:.*", RegexOptions.Compiled);
        private static readonly Regex TableT0Regex = new Regex(@"\""type\"":\""Table\"",\""id\"":\""T\.0\"",""content\"":""(.*?)""", RegexOptions.Compiled);
        private static readonly Regex ProfileP1Regex = new Regex(@"\""type\"":\""Profile\"",\""id\"":\""P\.1\.0\""[^{]*\""content\"":\""(.*?)\""", RegexOptions.Compiled);
        private static readonly Regex ProfileP14Regex = new Regex(@"\""type\"":\""Profile\"",\""id\"":\""P\.14\.0\""[^{]*\""content\"":""(.*?)""", RegexOptions.Compiled);
        private static readonly Regex ProfileP2Regex = new Regex(@"\""type\"":\""Profile\"",\""id\"":\""P\.2\.0\""[^{]*\""content\"":""(.*?)""", RegexOptions.Compiled);
        private static readonly Regex AllTextRegex = new Regex(@".*", RegexOptions.Compiled);

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            FilteredMessages = CollectionViewSource.GetDefaultView(AllMessages);
            FilteredMessages.Filter = FilterMessages;
        }

        private void UpdateProcessButtonState()
        {
            ProcessButton.IsEnabled = !string.IsNullOrEmpty(TextInputEditor.Text) || !string.IsNullOrEmpty(selectedFilePath);
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateProcessButtonState();
        }

        private async void OnChooseFileClicked(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
            {
                selectedFilePath = openFileDialog.FileName;
                LogMessages.Add($"Plik wybrany: {selectedFilePath}");
                UpdateProcessButtonState();
            }
        }

        private async void OnDecryptFileClicked(object sender, RoutedEventArgs e)
        {
            LogMessages.Add("Rozpoczynam deszyfrowanie...");
            try
            {
                string content = !string.IsNullOrEmpty(selectedFilePath)
                    ? await File.ReadAllTextAsync(selectedFilePath)
                    : TextInputEditor.Text;

                decryptedLog = await DecryptTextAsync(content);
                decryptedLog = decryptedLog.Replace("\\n", Environment.NewLine);

                await LoadMessagesAsync(decryptedLog);

                Console.WriteLine($"Liczba wiadomości: {FilteredMessages.Cast<object>().Count()}");
            }
            catch (Exception ex)
            {
                LogMessages.Add($"Błąd: {ex.Message}");
            }
        }

        private async Task LoadMessagesAsync(string content)
        {
            AllMessages.Clear();
            foreach (var line in content.Split(Environment.NewLine, StringSplitOptions.None))
            {
                AllMessages.Add(line);
            }
            FilteredMessages.Refresh();
        }

        private async Task<string> DecryptTextAsync(string encryptedString)
        {
            try
            {
                byte[] compressedData = Convert.FromBase64String(encryptedString);

                using (MemoryStream memoryStream = new MemoryStream(compressedData))
                using (GZipStream gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
                using (StreamReader reader = new StreamReader(gzipStream, Encoding.UTF8))
                {
                    LogMessages.Add("Rozpakowywanie gzip...");
                    string decompressedText = await reader.ReadToEndAsync();
                    LogMessages.Add("Zakończono rozpakowywanie gzip.");
                    return DecodeBase64Fragments(decompressedText);
                }
            }
            catch (Exception ex)
            {
                LogMessages.Add($"Błąd podczas deszyfrowania: {ex.Message}");
                return string.Empty;
            }
        }

        private string DecodeBase64Fragments(string input)
        {
            LogMessages.Add("Decodowanie fragmentów 'content'...");
            string base64Pattern = "\\\\\"content\\\\\":\\\\\"([^\"]+)\\\\\"";
            var matches = Regex.Matches(input, base64Pattern);

            var decodedFragments = input;
            LogMessages.Add($"Liczba fragmentów do zdekodowania: {matches.Count}");

            int matchIndex = 1;
            foreach (Match match in matches)
            {
                string base64String = match.Groups[1].Value;

                if (!string.IsNullOrWhiteSpace(base64String) && IsBase64String(base64String))
                {
                    try
                    {
                        byte[] data = Convert.FromBase64String(base64String);
                        string decodedString = Encoding.UTF8.GetString(data);
                        decodedFragments = decodedFragments.Replace(base64String, decodedString);
                        LogMessages.Add($"Fragment {matchIndex} zdekodowany.");
                    }
                    catch (Exception ex)
                    {
                        LogMessages.Add($"Błąd podczas dekodowania fragmentu {matchIndex}: {ex.Message}");
                    }
                }
                matchIndex++;
            }

            LogMessages.Add("Dekodowanie zakończone.");
            return decodedFragments;
        }

        private bool FilterMessages(object item)
        {
            var line = item as string;

            if (FilterAllCheckBox.IsChecked == true && AllTextRegex.IsMatch(line))
                return true;

            if (FilterLoggerCheckBox.IsChecked == true && LoggerRegex.IsMatch(line))
                return true;

            if (FilterAPDUCheckBox.IsChecked == true && ApduRegex.IsMatch(line))
                return true;

            if (FilterT0CheckBox.IsChecked == true && TableT0Regex.IsMatch(line))
                return true;

            if (FilterP1CheckBox.IsChecked == true && ProfileP1Regex.IsMatch(line))
                return true;

            if (FilterP1CheckBox.IsChecked == true && ProfileP1Regex.IsMatch(line))
                return true;

            if (FilterP2CheckBox.IsChecked == true && ProfileP2Regex.IsMatch(line))
                return true;

            if (FilterP14CheckBox.IsChecked == true && ProfileP14Regex.IsMatch(line))
                return true;

            return false;
        }

        private void OnFilterChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                if (FilterAllCheckBox != null)
                {
                    if (sender == FilterAllCheckBox)
                    {
                        if (FilterAllCheckBox.IsChecked == true)
                            EnableAllFilters();
                        else
                            DisableAllFilters();
                    }
                    else
                        UpdateFilterAllCheckBoxState();
                }

                FilteredMessages?.Refresh();
            }
            catch (Exception ex)
            {
                LogMessages.Add($"Błąd podczas generowania filtrów: {ex.Message}");
            }
        }

        private void EnableAllFilters()
        {
            FilterAPDUCheckBox.IsChecked = true;
            FilterP14CheckBox.IsChecked = true;
            FilterP1CheckBox.IsChecked = true;
            FilterP2CheckBox.IsChecked = true;
            FilterLoggerCheckBox.IsChecked = true;
            FilterT0CheckBox.IsChecked = true;
        }

        private void DisableAllFilters()
        {
            FilterAPDUCheckBox.IsChecked = false;
            FilterP14CheckBox.IsChecked = false;
            FilterP1CheckBox.IsChecked = false;
            FilterP2CheckBox.IsChecked = false;
            FilterLoggerCheckBox.IsChecked = false;
            FilterT0CheckBox.IsChecked = false;
        }

        private void UpdateFilterAllCheckBoxState()
        {
            bool allSelected = FilterAPDUCheckBox.IsChecked == true &&
                               FilterP14CheckBox.IsChecked == true &&
                               FilterP1CheckBox.IsChecked == true &&
                               FilterP2CheckBox.IsChecked == true &&
                               FilterLoggerCheckBox.IsChecked == true &&
                               FilterT0CheckBox.IsChecked == true;

            if (allSelected && FilterAllCheckBox.IsChecked != true)
                FilterAllCheckBox.IsChecked = true;
            else if (!allSelected && FilterAllCheckBox.IsChecked == true)
                FilterAllCheckBox.IsChecked = false;
        }

        private static bool IsBase64String(string input)
        {
            Span<byte> buffer = new Span<byte>(new byte[input.Length]);
            return Convert.TryFromBase64String(input, buffer, out _);
        }

        private async void OnSaveTextClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    FileName = $"DecryptedLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                    Filter = "Text files (*.txt)|*.txt"
                };
                if (saveFileDialog.ShowDialog() == true)
                {
                    await File.WriteAllTextAsync(saveFileDialog.FileName, decryptedLog);
                    MessageBox.Show($"Plik zapisany: {saveFileDialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                LogMessages.Add($"Błąd podczas zapisywania pliku: {ex.Message}");
            }
        }
    }
}
