using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Dispatches actions to the Unity main thread.
    /// Required for callbacks from background threads (e.g. GAID retrieval).
    /// </summary>
    public class UnityMainThreadDispatcher : MonoBehaviour
    {
        private static UnityMainThreadDispatcher _instance;
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        private static int _mainThreadId = -1;

        // Also creates the instance now: its getter builds a GameObject, which throws if
        // first touched from a background (e.g. Google Mobile Ads callback) thread.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CaptureMainThread()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            _ = Instance;
        }

        /// <summary>
        /// Runs the action inline on the main thread, or queues it for the next Update
        /// when called from any other thread. Use for every native SDK callback (GMA
        /// fires its ad events on a Java thread) before touching a Unity API.
        /// </summary>
        public static void RunOnMainThread(Action action)
        {
            if (action == null)
                return;

            if (_mainThreadId == -1 || Thread.CurrentThread.ManagedThreadId == _mainThreadId)
            {
                action();
                return;
            }

            Instance.Enqueue(action);
        }

        public static UnityMainThreadDispatcher Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("ZeyWinAds_MainThreadDispatcher");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<UnityMainThreadDispatcher>();
                }
                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            while (_queue.TryDequeue(out var action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception e)
                {
                    Logger.Error("MainThreadDispatcher error: {0}", e.Message);
                }
            }
        }

        /// <summary>
        /// Enqueues an action to be executed on the main thread.
        /// </summary>
        public void Enqueue(Action action)
        {
            if (action != null)
                _queue.Enqueue(action);
        }

        public void OnZeyWinAdsATTStatus(string status)
        {
            Logger.Debug("ATT authorization status: {0}", status);
            DeviceIdentity.OnATTStatusReceived(status);
            AppTrackingTransparency.HandleNativeStatus(status);
        }
    }
}
