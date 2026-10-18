using System;
using System.Collections.Generic;

namespace Pb.Core
{
    /// <summary>通过反射创建 Provider，避免 UI 与各 Provider 的编译期耦合。</summary>
    public static class ProviderRegistry
    {
        public static List<IProvider> CreateAll(AppConfig config, IAppContext ctx)
        {
            var list = new List<IProvider>();
            Add(list, "Pb.Core.Providers.LogitechHidppProvider", config, ctx);
            Add(list, "Pb.Core.Providers.StandardHidBatteryProvider", config, ctx);
            Add(list, "Pb.Core.Providers.BluetoothBatteryProvider", config, ctx);
            Add(list, "Pb.Core.Providers.XInputBatteryProvider", config, ctx);
            Add(list, "Pb.Core.Providers.GenericHidPeripheralProvider", config, ctx);
            Add(list, "Pb.Core.Providers.SimulationProvider", config, ctx);
            return list;
        }

        private static void Add(List<IProvider> list, string typeName, AppConfig config, IAppContext ctx)
        {
            Type type = typeof(ProviderRegistry).Assembly.GetType(typeName, false);
            if (type == null)
            {
                Log.Warn("未找到 Provider 类型: " + typeName + "（该功能暂不可用）");
                return;
            }
            try
            {
                var ctor = type.GetConstructor(new Type[] { typeof(AppConfig), typeof(IAppContext) });
                if (ctor == null)
                {
                    Log.Warn("Provider 缺少构造函数: " + typeName);
                    return;
                }
                var provider = (IProvider)ctor.Invoke(new object[] { config, ctx });
                list.Add(provider);
            }
            catch (Exception ex)
            {
                Log.Error("创建 Provider 失败: " + typeName, ex);
            }
        }
    }

    public sealed class AppContextAdapter : IAppContext
    {
        private readonly AppConfig _config;
        public AppContextAdapter(AppConfig config) { _config = config; }
        public AppConfig Config { get { return _config; } }
        public void Log(string message) { Pb.Core.Log.Info(message); }
    }
}