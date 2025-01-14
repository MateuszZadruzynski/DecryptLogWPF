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

namespace Deszyfrowanie_Logów
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<string> LogMessages { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<string> DataMessages { get; set; } = new ObservableCollection<string>();

        private string decryptedLog = string.Empty;
        private string selectedFilePath;
        private int currentPage = 0;
        private int totalPages = 0;
        private const int linesPerPage = 3000;

        private static readonly Regex LoggerRegex = new Regex(@"\[\w+Logger\]\s\d{2}:\d{2}:\d{2}\.\d{2}\s\d{5}\s[RW]\s.*", RegexOptions.Compiled);
        private static readonly Regex AttributeRegex = new Regex(@"Atrybut\s\d+\sdane:\s<\?xml.*?\/>", RegexOptions.Compiled);
        private static readonly Regex ApduRegex = new Regex(@"<- APDU:.*|-> APDU:.*", RegexOptions.Compiled);

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void UpdateProcessButtonState()
        {
            ProcessButton.IsEnabled = !string.IsNullOrEmpty(TextInputEditor.Text) || !string.IsNullOrEmpty(selectedFilePath);
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

        private void OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateProcessButtonState();
        }

        private async void OnDecryptFileClicked(object sender, RoutedEventArgs e)
        {
            LogMessages.Add("Rozpoczynam deszyfrowanie...");
            if (!string.IsNullOrEmpty(selectedFilePath))
            {
                await DecryptAndDisplayFileAsync(selectedFilePath);
            }
            else if (!string.IsNullOrEmpty(TextInputEditor.Text))
            {
                decryptedLog = await DecryptTextAsync(TextInputEditor.Text);
                decryptedLog = decryptedLog.Replace("\\n", Environment.NewLine);
                totalPages = (int)Math.Ceiling((double)decryptedLog.Length / linesPerPage);
                DisplayDecryptedContentGradually(decryptedLog);
            }
            else
            {
                MessageBox.Show("Brak pliku lub tekstu do deszyfrowania.");
            }
        }

        private async Task DecryptAndDisplayFileAsync(string filePath)
        {
            try
            {
                string fileContent = await File.ReadAllTextAsync(filePath);
                decryptedLog = await DecryptTextAsync(fileContent);
                totalPages = (int)Math.Ceiling((double)decryptedLog.Length / linesPerPage);
                DisplayDecryptedContentGradually(decryptedLog);
            }
            catch (Exception ex)
            {
                LogMessages.Add($"Błąd podczas deszyfrowania pliku: {ex.Message}");
            }
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
                    string decompressedText = await reader.ReadToEndAsync();
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
            string base64Pattern = "\\\\\"content\\\\\":\\\\\"([^\"]+)\\\\\"";
            var matches = Regex.Matches(input, base64Pattern);
            var decodedFragments = input;

            foreach (Match match in matches)
            {
                string base64String = match.Groups[1].Value;
                if (IsBase64String(base64String))
                {
                    try
                    {
                        byte[] data = Convert.FromBase64String(base64String);
                        string decodedString = Encoding.UTF8.GetString(data);
                        decodedFragments = decodedFragments.Replace(base64String, decodedString);
                    }
                    catch (Exception)
                    {
                        LogMessages.Add("Błąd podczas dekodowania fragmentu.");
                    }
                }
            }

            return decodedFragments;
        }

        private static bool IsBase64String(string input)
        {
            Span<byte> buffer = new Span<byte>(new byte[input.Length]);
            return Convert.TryFromBase64String(input, buffer, out int bytesParsed);
        }

        private void DisplayDecryptedContentGradually(string decryptedContent)
        {
            var lines = decryptedContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            int totalLines = lines.Length;

            int startLine = currentPage * linesPerPage;
            int endLine = Math.Min(startLine + linesPerPage, totalLines);

            DataMessages.Clear();
            for (int i = startLine; i < endLine; i++)
            {
                DataMessages.Add(lines[i]);
            }

            if (endLine >= totalLines)
            {
                LogMessages.Add("Osiągnięto koniec danych.");
            }

            PageIndicator.Text = $"{currentPage + 1}/{totalPages}";
        }

        private void OnPreviousPageButtonClicked(object sender, RoutedEventArgs e)
        {
            if (currentPage > 0)
            {
                currentPage--;
                DisplayDecryptedContentGradually(decryptedLog);
            }
            else
            {
                LogMessages.Add("Jesteś na pierwszej stronie.");
            }
        }

        private void OnNextPageButtonClicked(object sender, RoutedEventArgs e)
        {
            if (currentPage < totalPages - 1)
            {
                currentPage++;
                DisplayDecryptedContentGradually(decryptedLog);
            }
            else
            {
                LogMessages.Add("Jesteś na ostatniej stronie.");
            }
        }

        private async void OnSaveTextClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                string fileName = $"DecryptedLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                var saveFileDialog = new SaveFileDialog
                {
                    FileName = fileName,
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
