using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ganss.Xss;
using Newtonsoft.Json.Linq;
using TinyNvidiaUpdateChecker;

public class GpuDevice
{
    public string id { get; set; }
    public string vendorid { get; set; }
    public string ssid { get; set; }
    public string svid { get; set; }
    public string name { get; set; }
}

public class DriverVersion
{
    public string key { get; set; }
    public string version { get; set; }
    public List<string> os { get; set; }
    public string bit { get; set; }
    public string type { get; set; }
    public int dch { get; set; }
    public string notebook { get; set; }
    public string variant { get; set; }
    public List<int> supports { get; set; }
}

public class CombinedGpuData
{
    public Dictionary<string, GpuDevice> devices { get; set; }
    public List<DriverVersion> versions { get; set; }
}

/// <summary>
/// This class handles the retrieval and processing of GPU metadata provided by TechPowerUp
/// </summary>
public class NewMetadataHandler
{
    private static CombinedGpuData _combinedGpuData;

    public static bool LoadCombinedJsonData()
    {
        try
        {
            string jsonString = MainConsole.SendGetRequest(MainConsole.experimentalGpuMetadataRepo);
            _combinedGpuData = JsonSerializer.Deserialize<CombinedGpuData>(jsonString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return _combinedGpuData?.devices != null && _combinedGpuData.versions != null;
        }
        catch
        {
            return false;
        }
    }

    public static (GpuDevice matchedGpu, int deviceKey) FindGpuDetailsByDeviceId(string deviceId)
    {
        if (_combinedGpuData?.devices == null || string.IsNullOrWhiteSpace(deviceId)) return (null, 0);
        foreach (KeyValuePair<string, GpuDevice> entry in _combinedGpuData.devices)
        {
            GpuDevice device = entry.Value;
            if (device?.id != null && device.id.Equals(deviceId, StringComparison.OrdinalIgnoreCase) && int.TryParse(entry.Key, out int key))
            {
                return (device, key);
            }
        }
        return (null, 0);
    }

    public static DriverVersion FindLatestDriverForGpu(int gpuIndex, string driverType)
    {
        DriverVersion latestDriver = null;
        Version latestParsedVersion = new(0, 0);
        DriverVersion latestNotebookDriver = null;
        Version latestNotebookVersion = new(0, 0);
        bool preferNotebook = driverType != "sd" && IsMobileGpuIndex(gpuIndex);

        if (_combinedGpuData?.versions == null) return null;
        foreach (var driver in _combinedGpuData.versions)
        {
            if (driver?.supports?.Contains(gpuIndex) == true)
            {
                // Does driver type match? For GRD, prefer notebook variants for mobile GPUs.
                bool isStudio = string.Equals(driver.type, "Studio", StringComparison.OrdinalIgnoreCase);
                bool isNotebookVariant = string.Equals(driver.notebook, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(driver.type, "Notebook", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(driver.variant, "notebook", StringComparison.OrdinalIgnoreCase)
                    || driver.key?.Contains("notebook", StringComparison.OrdinalIgnoreCase) == true;

                if ((driverType == "sd" && isStudio) || (driverType != "sd" && !isStudio))
                {
                    if (Version.TryParse(driver.version, out Version currentParsedVersion))
                    {
                        if (preferNotebook && isNotebookVariant && currentParsedVersion > latestNotebookVersion)
                        {
                            latestNotebookVersion = currentParsedVersion;
                            latestNotebookDriver = driver;
                        }
                        if (currentParsedVersion > latestParsedVersion)
                        {
                            latestParsedVersion = currentParsedVersion;
                            latestDriver = driver;
                        }
                    }
                }
            }
        }
        return latestNotebookDriver ?? latestDriver;
    }

    private static bool IsMobileGpuIndex(int gpuIndex)
    {
        return _combinedGpuData?.devices?.TryGetValue(gpuIndex.ToString(), out GpuDevice device) == true
            && (device.name?.Contains("Laptop", StringComparison.OrdinalIgnoreCase) == true
                || device.name?.Contains("Notebook", StringComparison.OrdinalIgnoreCase) == true
                || device.name?.Contains("Max-Q", StringComparison.OrdinalIgnoreCase) == true);
    }

    public static (DriverMetadata metadata, string errorCode) GetDriverMetadata(string deviceId, string driverType)
    {
        if (!LoadCombinedJsonData()) return (null, "Error parsing GPU metadata json.");
        (GpuDevice matchedGpu, int gpuIndex) = FindGpuDetailsByDeviceId(deviceId);

        if (matchedGpu != null)
        {
            DriverVersion latestDriver = FindLatestDriverForGpu(gpuIndex, driverType);

            if (latestDriver != null)
            {
                string downloadUrl = $"https://international.download.nvidia.com/Windows/{latestDriver.version}/{latestDriver.key}.exe";
                string pdfUrl = $"https://international.download.nvidia.com/Windows/{latestDriver.version}/{latestDriver.version}-win11-win10-release-notes.pdf";

                // Query release date and file size
                using (var request = new HttpRequestMessage(HttpMethod.Head, downloadUrl))
                {
                    using var response = MainConsole.SendMetadataRequest(request);
                    response.EnsureSuccessStatusCode();

                    // File size
                    long fileSize = response.Content.Headers.ContentLength ?? 0;

                    // Release date
                    DateTimeOffset? releaseDateOffset = response.Content.Headers.LastModified;
                    DateTime releaseDate = releaseDateOffset?.LocalDateTime ?? DateTime.MinValue;

                    // Test if PDF url is OK
                    if (!IsUrlOk(pdfUrl)) pdfUrl = null;

                    // Release notes
                    string releaseNotes = RetrieveReleaseNotes();

                    return (new DriverMetadata(latestDriver.key, latestDriver.version, fileSize, latestDriver.type, downloadUrl, pdfUrl, releaseNotes, releaseDate), null);
                }
            }
            else
            {
                // TODO implement popup, and ask to revert to GRD
                string error = "No compatible driver was found for your GPU.";
                if (driverType == "sd")
                {
                    error += "\nYou have opted to only recieve Studio drivers. Perhaps your GPU has no available Studio drivers.";
                }
                return (null, error);
            }
        }
        else
        {
            return (null, $"Your GPU is not supported by this experimental repo. Your device ID: {deviceId}");
        }
    }

    private static bool IsUrlOk(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = MainConsole.SendMetadataRequest(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string RetrieveReleaseNotes()
    {
        try
        {
            // Parse code
            string releaseNotes = MainConsole.SendGetRequest(MainConsole.experimentalGpuMetadataRepoReleaseNotes);
            JObject parsed = JObject.Parse(releaseNotes);
            string html = parsed["result"].ToString();

            // Remove download forms
            html = Regex.Replace(html, @"<form[^>]*action\s*=\s*[""']?/download/.*?</form>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Only get the three latest release notes
            MatchCollection matches = Regex.Matches(html, @"<div class=""version[^""]*"" id=""changes-[^""]+"">.*?<\/div>", RegexOptions.Singleline);
            string limitedHtml = "";

            for (int i = 0; i < Math.Min(3, matches.Count); i++)
            {
                limitedHtml += matches[i].Value;
            }

            // Sanitize
            HtmlSanitizer sanitizer = new HtmlSanitizer();
            string sanitizedHtml = sanitizer.Sanitize(limitedHtml);

            string finalHtml = $"<html><head><meta charset=\"UTF-8\"></head><body>{sanitizedHtml}</body></html>";
            return finalHtml;
        }
        catch
        {
            return "Unable to retrieve release notes.";
        }
    }
}