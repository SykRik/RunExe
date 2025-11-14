using System;
using System.Collections.Generic;
using System.ComponentModel;   // Win32Exception
using System.Diagnostics;     // Process
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

public sealed class Manager : MonoBehaviour
{
    [Header("UI Title")]
    [SerializeField] private TMP_Text tmpTextTitle;

    [Header("Buttons")]
    [SerializeField] private Transform sectionButton;   // Container for item buttons
    [SerializeField] private Item itemPrefab;           // Prefab for each lesson button

    [Header("Error Panel")]
    [SerializeField] private ScrollRect errorScroll;
    [SerializeField] private TMP_Text tmpTextError;     // Error text prefab

    #region ===== Unity Lifecycle =====

    private void Start()
    {
        try
        {
            CreateButtons();
        }
        catch (Exception ex)
        {
            LogError($"Exception in Start: {ex}");
            ShowError($"Initialization error: {ex.Message}");
        }
    }

    #endregion

    #region ===== Core =====

    private void CreateButtons()
    {
        LogInfo("CreateButtons()");

        var appData = DataUtils.GetData<AppData>("app_data");
        var items   = DataUtils.GetDatas<ItemData>("item_data");

        if (appData == null)
        {
            LogError("appData is null.");
            ShowError("App metadata (app_data) is missing or invalid.");
            return;
        }

        if (items == null || items.Count == 0)
        {
            LogError("item_data is null or empty.");
            ShowError("Item metadata (item_data) is missing or invalid.");
            return;
        }

        if (tmpTextTitle != null)
            tmpTextTitle.text = appData.title;

        // Directory that contains the launcher executable (App A)
        var exeDir = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        LogInfo($"exeDir = {exeDir}");

        // Resolve startPath (absolute / relative / empty)
        var startPath = ResolveStartPath(appData.startPath, exeDir);
        LogInfo($"FINAL startPath = {startPath}");

        // Scan all .exe files under startPath
        var exes = FindExeFiles(startPath);
        LogInfo($"Found {exes.Count} distinct exe-name keys under {startPath}");

        // Clear previous buttons if any
        if (sectionButton != null)
        {
            for (int i = sectionButton.childCount - 1; i >= 0; i--)
                Destroy(sectionButton.GetChild(i).gameObject);
        }

        // Create a button for each ItemData
        foreach (var item in items)
        {
            if (item == null)
                continue;

            if (itemPrefab == null || sectionButton == null)
            {
                LogError("itemPrefab or sectionButton is null.");
                break;
            }

            var button = Instantiate(itemPrefab, sectionButton);
            var sprite = GetSprite(item.imageName);

            if (exes.TryGetValue(item.exeName, out var pathList) && pathList.Count > 0)
            {
                string exePath = pathList[0];

                LogInfo($"Mapping item \"{item.text}\" => {exePath}");

                button.Setup(item.text, () => RunExe(exePath), sprite);

                if (pathList.Count > 1)
                {
                    var msg = $"There are {pathList.Count} executable files with the same name: {item.exeName}";
                    LogWarning(msg);
                    ShowError(msg);
                }
            }
            else
            {
                // Executable not found → keep button visible but without an action
                var msg = $"Executable file not found: {item.exeName}";
                button.Setup(item.text, null, sprite);
                LogWarning($"No executable found for item.exeName = {item.exeName}");
                ShowError(msg);
            }
        }
    }

    /// <summary>
    /// Resolve startPath loaded from CSV:
    /// - If empty: prefer "Programs" subfolder next to launcher; otherwise use exeDir.
    /// - If absolute path (C:\...): use it if it exists, otherwise fall back to exeDir.
    /// - If relative path (e.g., "Programs\Chem"): combine with exeDir and validate.
    /// </summary>
    private string ResolveStartPath(string rawStartPath, string exeDir)
    {
        LogInfo($"rawStartPath = \"{rawStartPath}\"");

        // 1) Empty → try default "Programs" folder
        if (string.IsNullOrWhiteSpace(rawStartPath))
        {
            string defaultRelative = "Programs";
            string candidate = Path.GetFullPath(Path.Combine(exeDir, defaultRelative));

            if (Directory.Exists(candidate))
            {
                LogInfo($"startPath is empty → using default \"{defaultRelative}\" => {candidate}");
                return candidate;
            }

            LogWarning($"startPath is empty and default folder \"Programs\" does not exist. Using exeDir = {exeDir}");
            return exeDir;
        }

        // 2) Absolute path?
        if (Path.IsPathRooted(rawStartPath))
        {
            string abs = rawStartPath;
            if (Directory.Exists(abs))
            {
                LogInfo($"startPath absolute OK: {abs}");
                return abs;
            }

            LogWarning($"startPath absolute does NOT exist: {abs}. Using exeDir = {exeDir}");
            return exeDir;
        }

        // 3) Relative path ("Programs", "Programs\\Chemistry", ...)
        string combined = Path.GetFullPath(Path.Combine(exeDir, rawStartPath));
        if (Directory.Exists(combined))
        {
            LogInfo($"startPath relative \"{rawStartPath}\" => {combined}");
            return combined;
        }

        LogWarning($"startPath relative \"{rawStartPath}\" does NOT exist (tested {combined}). Using exeDir = {exeDir}");
        return exeDir;
    }

    #endregion

    #region ===== Run Exe (via CMD) =====

    /// <summary>
    /// Launch an executable via CMD:
    /// cmd.exe /c start "" "fullPath" [args]
    /// - Bypasses most SmartScreen / Smart App Control blocks on direct Process.Start.
       /// - Does not show a console window.
    /// </summary>
    public void RunExe(string exePath, string args = "")
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            LogError("RunExe: exePath is null or empty.");
            ShowError("Executable path is empty.");
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(exePath);
            LogInfo($"RunExe (CMD): fullPath = {fullPath}");

            if (!File.Exists(fullPath))
            {
                var msg = $"Executable file does not exist: {fullPath}";
                LogError(msg);
                ShowError(msg);
                return;
            }

            // Prepare arguments for cmd:
            // start "" "fullPath" args
            var workingDir = Path.GetDirectoryName(fullPath) ?? string.Empty;
            var cmdArgs = $"/c start \"\" \"{fullPath}\" {args}".TrimEnd();

            var psi = new ProcessStartInfo
            {
                FileName         = "cmd.exe",
                Arguments        = cmdArgs,
                UseShellExecute  = false,
                CreateNoWindow   = true,
                WorkingDirectory = workingDir
            };

            var p = Process.Start(psi);

            if (p != null)
            {
                LogInfo($"CMD launched successfully (PID={p.Id}). Executable will be started by 'start' command.");
            }
            else
            {
                var msg = "Failed to start CMD process to launch executable (Process.Start returned null).";
                LogError(msg);
                ShowError(msg);
            }
        }
        catch (Win32Exception ex)
        {
            var msg = $"Failed to launch program (Win32): {ex.Message}";
            LogError($"RunExe via CMD failed (Win32Exception {ex.NativeErrorCode}): {ex.Message}");
            ShowError(msg);
        }
        catch (Exception ex)
        {
            var msg = $"Failed to launch program: {ex.Message}";
            LogError($"RunExe via CMD failed: {ex.GetType().Name} - {ex.Message}");
            ShowError(msg);
        }
    }

    #endregion

    #region ===== Helpers =====

    private Sprite GetSprite(string spriteName)
    {
        if (string.IsNullOrWhiteSpace(spriteName))
            return null;

        // 1) Try Resources/Images first
        Sprite sprite = Resources.Load<Sprite>($"Images/{spriteName}");
        if (sprite != null)
            return sprite;

        // 2) Fallback: StreamingAssets
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, spriteName + ".png");
            if (File.Exists(path))
            {
                byte[] bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2);
                if (tex.LoadImage(bytes))
                {
                    sprite = Sprite.Create(
                        tex,
                        new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f));
                    LogInfo($"Loaded sprite from StreamingAssets: {path}");
                    return sprite;
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"GetSprite exception: {ex}");
        }

        ShowError($"Image file not found: {spriteName}");
        return null;
    }

    /// <summary>
    /// Scan all .exe files in rootPath and its subfolders.
    /// Returns a dictionary: key = file name (xxx.exe), value = list of full paths.
    /// </summary>
    private Dictionary<string, List<string>> FindExeFiles(string rootPath)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            LogWarning($"FindExeFiles: rootPath does not exist: {rootPath}");
            return result;
        }

        try
        {
            LogInfo($"Scanning executable files under: {rootPath}");

            var files = Directory.GetFiles(rootPath, "*.exe", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file); // file name + .exe only
                if (!result.TryGetValue(fileName, out var list))
                {
                    list = new List<string>();
                    result[fileName] = list;
                }
                list.Add(file);
            }

            LogInfo($"FindExeFiles: found {files.Length} executable file(s).");
        }
        catch (Exception ex)
        {
            LogError($"FindExeFiles exception: {ex}");
            ShowError($"Error while scanning executable files: {ex.Message}");
        }

        return result;
    }

    private void ShowError(string error, bool isActive = true)
    {
        LogError(error);

        if (tmpTextError != null && errorScroll != null)
        {
            var newError = Instantiate(tmpTextError, errorScroll.content);
            newError.text = error;
            errorScroll.gameObject.SetActive(isActive);
        }
    }

    public void Quit()
    {
        LogInfo("Quit called.");
        Application.Quit();
    }

    #endregion

    #region ===== Logging helpers =====

    private const string LOG_TAG = "[Manager]";

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void LogInfo(string message)
    {
        Debug.Log($"{LOG_TAG} [INFO] {message}");
    }

    private void LogWarning(string message)
    {
        Debug.LogWarning($"{LOG_TAG} [WARN] {message}");
    }

    private void LogError(string message)
    {
        Debug.LogError($"{LOG_TAG} [ERROR] {message}");
    }

    #endregion
}
