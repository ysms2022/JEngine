using System;
using System.Collections.Generic;
using System.IO;
using BM;
using ET;
using JEngine.Core;
using UnityEngine;

public interface IUpdater
    {
        void OnMessage(string msg);

        void OnProgress(float progress);

        void OnVersion(string ver);

        void OnLoadSceneProgress(float progress);

        void OnLoadSceneFinish();

        void OnUpdateFailed();
    }

    public class BaseUpdater : IUpdater
    {
        public Action<string> onMessage;
        public void OnMessage(string msg)
        {
            onMessage?.Invoke(msg);
        }

        public Action<float> onProgress;
        public void OnProgress(float progress)
        {
            onProgress?.Invoke(progress);
        }
        
        public Action<string> onVersion;
        public void OnVersion(string ver)
        {
            onVersion?.Invoke(ver);
        }

        public Action<float> onLoadSceneProgress;
        public void OnLoadSceneProgress(float progress)
        {
            onLoadSceneProgress?.Invoke(progress);
        }

        public Action onLoadSceneFinish;
        public void OnLoadSceneFinish()
        {
            onLoadSceneFinish?.Invoke();
        }
        
        public Action onUpdateFailed;
        public void OnUpdateFailed()
        {
            onUpdateFailed?.Invoke();
        }
        

        public BaseUpdater(Action<string> onMessage, Action<float> onProgress, Action<string> onVersion, Action<float> onLoadSceneProgress, Action onLoadSceneFinish, Action onUpdateFailed)
        {
            this.onMessage = onMessage;
            this.onProgress = onProgress;
            this.onVersion = onVersion;
            this.onLoadSceneProgress = onLoadSceneProgress;
            this.onLoadSceneFinish = onLoadSceneFinish;
            this.onUpdateFailed = onUpdateFailed;
        }
    }

public class Updater : MonoBehaviour
{
    [SerializeField] private string baseURL = "http://127.0.0.1:7888/DLC/";
    [SerializeField] private string gameScene = "Assets/HotUpdateResources/Scene/Game.unity";
    [SerializeField] private string mainPackageName = "Main";

    [Tooltip("主包秘钥，如果加密了的话需要填写")] [SerializeField]
    private string mainPackageKey = "";
    [Tooltip("主包是否需要校验CRC")] [SerializeField]
    private bool mainPackageCheckCRC = true;

    [Tooltip("Develop是开发模式，Local是离线模式，Build是真机模式")] [SerializeField]
    private AssetLoadMode mode = AssetLoadMode.Develop;

    /// <summary>
    /// 获取分包信息
    /// </summary>
    /// <param name="bundlePackageName">包名</param>
    /// <param name="checkCRC"></param>
    public static async ETTask<UpdateBundleDataInfo> CheckPackage(string bundlePackageName, bool checkCRC = true)
    {
        return await AssetComponent.CheckAllBundlePackageUpdate(new Dictionary<string, bool>()
        {
            { bundlePackageName, checkCRC }
        });
    }

    /// <summary>
    /// 获取本地包版本信息（没下载过就是0）
    /// </summary>
    /// <param name="bundlePackageName">包名</param>
    /// <param name="package">分包信息（可以留空，自动根据包名获取）</param>
    /// <returns></returns>
    public static async ETTask<int> GetLocalPackageVersion(string bundlePackageName,
        UpdateBundleDataInfo package = null)
    {
        package = package ?? await CheckPackage(bundlePackageName, false);
        var ver = package.GetVersion(bundlePackageName);
        if (ver == null)
        {
            throw new InvalidOperationException("Can not find local package version");
        }
        return ver[0];
    }

    /// <summary>
    /// 获取远程包版本信息
    /// </summary>
    /// <param name="bundlePackageName">包名</param>
    /// <param name="package">分包信息（可以留空，自动根据包名获取）</param>
    /// <returns></returns>
    public static async ETTask<int> GetRemotePackageVersion(string bundlePackageName,
        UpdateBundleDataInfo package = null)
    {
        package = package ?? await CheckPackage(bundlePackageName, false);
        var ver = package.GetVersion(bundlePackageName);
        if (ver == null)
        {
            throw new InvalidOperationException("Can not find remote package version");
        }
        return ver[1];
    }

    /// <summary>
    /// 下载包
    /// </summary>
    /// <param name="bundlePackageName">包名</param>
    /// <param name="updater"></param>
    /// <param name="checkCRC"></param>
    /// <param name="package"></param>
    /// <param name="key"></param>
    /// <param name="nextScene"></param>
    public static async void UpdatePackage(string bundlePackageName, IUpdater updater, bool checkCRC = true,
        UpdateBundleDataInfo package = null, string key = null, string nextScene = null)
    {
        if (string.IsNullOrEmpty(key)) key = null;
        MessageBox.Dispose();
        package = package ?? await CheckPackage(bundlePackageName, checkCRC);

        try
        {
            updater.OnVersion("资源版本号: v" + Application.version + "res" +
                              await GetRemotePackageVersion(bundlePackageName, package));
        }
        //can not find remote version
        catch (InvalidOperationException)
        {
            var mb = MessageBox.Show("错误", "无法获取服务器信息", "返回", "退出");

            void ONComplete(MessageBox.EventId ok)
            {
                if (ok == MessageBox.EventId.Ok)
                {
                    updater.OnUpdateFailed();
                }
                else
                {
                    Quit();
                }
            }
            mb.onComplete = ONComplete;
            return;
        }
        
        async void Init()
        {
            updater.OnProgress(1);
            updater.OnMessage("下载完成");
            await AssetComponent.Initialize(bundlePackageName, key);
            if (string.IsNullOrEmpty(nextScene)) return;
            updater.OnMessage("加载场景");
            AssetMgr.LoadSceneAsync(nextScene, false, bundlePackageName, updater.OnLoadSceneProgress,
                ao => updater.OnLoadSceneFinish());
        }

        if (package.NeedUpdate && AssetComponentConfig.AssetLoadMode == AssetLoadMode.Build)
        {
            updater.OnMessage($"需要更新, 大小: {Tools.GetDisplaySize(package.NeedUpdateSize)}");
            var tips = $"发现{package.NeedDownLoadBundleCount}个资源有更新，总计需要下载 {Tools.GetDisplaySize(package.NeedUpdateSize)}";
            var mb = MessageBox.Show("提示", tips, "下载", "退出");

            async void ONComplete(MessageBox.EventId ok)
            {
                if (ok == MessageBox.EventId.Ok)
                {
                    long now = Tools.TimeStamp;
                    package.OnProgress = info =>
                    {
                        float diff = Tools.TimeStamp - now;
                        diff = diff < 1 ? 1 : diff;
                        diff /= 1000;//s -> ms
                        var speed = info.FinishUpdateSize / diff;
                        updater.OnMessage(
                            $"下载中...{Tools.GetDisplaySpeed(speed)}, 进度：{Math.Round(info.Progress, 2)}%");
                        updater.OnProgress(info.Progress / 100f);
                    };
                    await AssetComponent.DownLoadUpdate(package);
                    Init();
                }
                else
                {
                    Quit();
                }
            }
            mb.onComplete = ONComplete;
        }
        else
        {
            Init();
        }
    }

    /// <summary>
    /// 下载包
    /// </summary>
    /// <param name="bundlePackageName"></param>
    /// <param name="updater"></param>
    /// <param name="checkCRC"></param>
    /// <param name="key"></param>
    /// <param name="nextScene"></param>
    public static void UpdatePackage(string bundlePackageName, IUpdater updater, bool checkCRC = true,
        string key = null,
        string nextScene = null)
    {
        UpdatePackage(bundlePackageName, updater, checkCRC, null, key, nextScene);
    }

    /// <summary>
    /// 下载包
    /// </summary>
    /// <param name="bundlePackageName">包名</param>
    /// <param name="checkCRC"></param>
    /// <param name="package"></param>
    /// <param name="key"></param>
    /// <param name="nextScene"></param>
    /// <param name="onMessage"></param>
    /// <param name="onProgress"></param>
    /// <param name="onVersion"></param>
    /// <param name="onLoadSceneProgress"></param>
    /// <param name="onLoadSceneFinished"></param>
    /// <param name="onUpdateFailed"></param>
    public static void UpdatePackage(string bundlePackageName, bool checkCRC = true,
        UpdateBundleDataInfo package = null, string key = null,
        string nextScene = null,
        Action<string> onMessage = null, Action<float> onProgress = null, Action<string> onVersion = null,
        Action<float> onLoadSceneProgress = null, Action onLoadSceneFinished = null, Action onUpdateFailed = null)
    {
        BaseUpdater updater =
            new BaseUpdater(onMessage, onProgress, onVersion, onLoadSceneProgress, onLoadSceneFinished, onUpdateFailed);
        UpdatePackage(bundlePackageName, updater, checkCRC, package, key, nextScene);
    }

    /// <summary>
    /// 删除缓存的包
    /// </summary>
    /// <param name="bundlePackageName"></param>
    public static void ClearPackage(string bundlePackageName)
    {
        var dir = Path.Combine(Application.persistentDataPath, "bundlePackageName");
        if(Directory.Exists(dir))
        {
            Directory.Delete(dir);
        }
    }

    /// <summary>
    /// 更新配置
    /// </summary>
    private void Awake()
    {
        baseURL = baseURL.EndsWith("/") ? baseURL : baseURL + "/";
        AssetComponentConfig.AssetLoadMode = mode;
        AssetComponentConfig.BundleServerUrl = baseURL;
        AssetComponentConfig.DefaultBundlePackageName = mainPackageName;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// 下载更新
    /// </summary>
    public void StartUpdate()
    {
        UpdatePackage(mainPackageName, FindObjectOfType<UpdateScreen>(), mainPackageCheckCRC, mainPackageKey,
            gameScene);
    }

    private void OnDestroy()
    {
        MessageBox.Dispose();
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
    }
}