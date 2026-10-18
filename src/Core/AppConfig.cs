using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;

namespace Pb.Core
{
    public sealed class AppConfig
    {
        public int RefreshSeconds { get; set; }
        public bool StartWithWindows { get; set; }
        public bool NotifyLowBattery { get; set; }
        public int LowBatteryThreshold { get; set; }
        public string TrayDisplay { get; set; }      // worst | average | highest
        public bool RunInBackground { get; set; }
        public bool EnableLogitech { get; set; }
        public bool EnableStandardHid { get; set; }
        public bool EnableBluetooth { get; set; }
        public bool EnableXInput { get; set; }
        public bool EnableDemo { get; set; }
        public bool VerboseLog { get; set; }
        public bool HasCompletedFirstRun { get; set; }

        public AppConfig()
        {
            RefreshSeconds = 30;
            StartWithWindows = true;
            NotifyLowBattery = true;
            LowBatteryThreshold = 15;
            TrayDisplay = "worst";
            RunInBackground = true;
            EnableLogitech = true;
            EnableStandardHid = true;
            EnableBluetooth = true;
            EnableXInput = true;
            EnableDemo = false;
            VerboseLog = false;
            HasCompletedFirstRun = false;
        }

        private static string _dataDir;
        private static readonly object DirSync = new object();

        /// <summary>
        /// 配置/日志目录。优先 %APPDATA%\PeripheralBattery；若不可写（受限环境/绿色便携），
        /// 退回到 exe 同目录的 data\，再不行退到临时目录，保证设置永远能保存。
        /// </summary>
        [ScriptIgnore]
        public static string DataDir
        {
            get
            {
                if (_dataDir != null) return _dataDir;
                lock (DirSync)
                {
                    if (_dataDir != null) return _dataDir;
                    string preferred = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PeripheralBattery");
                    if (EnsureWritable(preferred)) { _dataDir = preferred; return _dataDir; }

                    string local = null;
                    try
                    {
                        string exe = Assembly.GetEntryAssembly() != null ? Assembly.GetEntryAssembly().Location : null;
                        if (!string.IsNullOrEmpty(exe))
                            local = Path.Combine(Path.GetDirectoryName(exe), "data");
                    }
                    catch { }
                    if (!string.IsNullOrEmpty(local) && EnsureWritable(local)) { _dataDir = local; return _dataDir; }

                    string temp = Path.Combine(Path.GetTempPath(), "PeripheralBattery");
                    EnsureWritable(temp);
                    _dataDir = temp;
                    return _dataDir;
                }
            }
        }

        private static bool EnsureWritable(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        [ScriptIgnore]
        public static string ConfigPath { get { return Path.Combine(DataDir, "config.json"); } }

        public AppConfig Normalize()
        {
            if (RefreshSeconds < 5) RefreshSeconds = 5;
            if (RefreshSeconds > 3600) RefreshSeconds = 3600;
            if (LowBatteryThreshold < 1) LowBatteryThreshold = 1;
            if (LowBatteryThreshold > 90) LowBatteryThreshold = 90;
            if (TrayDisplay != "average" && TrayDisplay != "highest") TrayDisplay = "worst";
            return this;
        }

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var ser = new JavaScriptSerializer();
                    var cfg = ser.Deserialize<AppConfig>(json);
                    if (cfg != null) return cfg.Normalize();
                }
            }
            catch { }
            return new AppConfig().Normalize();
        }

        public void Save()
        {
            try
            {
                var ser = new JavaScriptSerializer();
                File.WriteAllText(ConfigPath, ser.Serialize(this));
            }
            catch { }
        }
    }
}