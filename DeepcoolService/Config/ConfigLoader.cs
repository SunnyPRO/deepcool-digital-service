using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace DeepcoolService.Config
{
    public static class ConfigLoader
    {
        private static readonly string ConfigFileName = "DeepcoolDisplay.cfg";

        public static IDictionary<string,string> Load()
        {
            var dict = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string path = Path.Combine(baseDir ?? ".", ConfigFileName);
                if (!File.Exists(path)) return dict; // no config file

                var lines = File.ReadAllLines(path);
                foreach (var raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    if (line.StartsWith("#") || line.StartsWith("//")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    dict[key] = value;
                }

                Apply(dict);
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("Config load failed: " + ex.Message);
            }
            return dict;
        }

        private static void Apply(IDictionary<string,string> values)
        {
            // Map config keys to environment variables used elsewhere for minimal code changes.
            SetEnvIf(values, "CPU_MODE", "DEEPCOOL_CH_CPU_MODE");
            SetEnvIf(values, "GPU_MODE", "DEEPCOOL_CH_GPU_MODE");
            SetEnvIf(values, "ENABLE_GPU", "DEEPCOOL_CH_GPU");
            SetEnvIf(values, "TABLE_MODE", "DEEPCOOL_CH_TABLE");
            SetEnvIf(values, "DUAL_MODE", "DEEPCOOL_CH_DUAL");
            SetEnvIf(values, "UPDATE_MS", "DEEPCOOL_UPDATE_MS");
            SetEnvIf(values, "PACKET_LEN", "DEEPCOOL_PACKET_LEN");
            SetEnvIf(values, "ENDIAN", "DEEPCOOL_ENDIAN");
            SetEnvIf(values, "AUTO_TABLE_SEC", "DEEPCOOL_AUTO_TABLE_SEC");
            SetEnvIf(values, "VID", "DEEPCOOL_VID");
            SetEnvIf(values, "PID", "DEEPCOOL_PID");
            // GPU sensor overrides
            SetEnvIf(values, "GPU_LOAD_SENSOR", "DEEPCOOL_GPU_LOAD_SENSOR");
            SetEnvIf(values, "GPU_TEMP_SENSOR", "DEEPCOOL_GPU_TEMP_SENSOR");
            // Multi-GPU selection keys
            SetEnvIf(values, "GPU_INDEX", "DEEPCOOL_GPU_INDEX");
            SetEnvIf(values, "GPU_VENDOR", "DEEPCOOL_GPU_VENDOR");
            SetEnvIf(values, "GPU_SELECT", "DEEPCOOL_GPU_SELECT");
            // Power display & scaling
            SetEnvIf(values, "CPU_POWER_MAX", "DEEPCOOL_CPU_POWER_MAX");
            SetEnvIf(values, "GPU_POWER_MAX", "DEEPCOOL_GPU_POWER_MAX");
            SetEnvIf(values, "CPU_MODE_CODE", "DEEPCOOL_CH_CPU_MODE_CODE");
            SetEnvIf(values, "GPU_MODE_CODE", "DEEPCOOL_CH_GPU_MODE_CODE");
            // Debug / diagnostics
            SetEnvIf(values, "TABLE_DEBUG", "DEEPCOOL_CH_TABLE_DEBUG");
            // Logging controls
            SetEnvIf(values, "LOG_LEVEL", "DEEPCOOL_LOG_LEVEL");
            SetEnvIf(values, "PACKET_SAMPLE", "DEEPCOOL_PACKET_SAMPLE");
            SetEnvIf(values, "MAX_LOG_MB", "DEEPCOOL_MAX_LOG_MB");
        }

        private static void SetEnvIf(IDictionary<string,string> dict, string key, string envName)
        {
            if (dict.TryGetValue(key, out string val) && !string.IsNullOrWhiteSpace(val))
            {
                Environment.SetEnvironmentVariable(envName, val);
            }
        }
    }
}
