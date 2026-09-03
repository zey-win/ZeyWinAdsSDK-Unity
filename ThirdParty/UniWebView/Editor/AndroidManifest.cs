using System.Xml;
using System.Collections;
using System.Text;
using System.IO;
using System;
using UnityEditor;
using UnityEngine;

internal class UniWebViewAndroidXmlDocument : XmlDocument {
    private string path;
    protected XmlNamespaceManager nameSpaceManager;
    public readonly string AndroidXmlNamespace = "http://schemas.android.com/apk/res/android";

    public UniWebViewAndroidXmlDocument(string path) {
        this.path = path;
        using (var reader = new XmlTextReader(path)) {
            reader.Read();
            Load(reader);
        }
        nameSpaceManager = new XmlNamespaceManager(NameTable);
        nameSpaceManager.AddNamespace("android", AndroidXmlNamespace);
    }

    public string Save() {
        return SaveAs(path);
    }

    public string SaveAs(string path) {
        using (var writer = new XmlTextWriter(path, new UTF8Encoding(false))) {
            writer.Formatting = Formatting.Indented;
            Save(writer);
        }
        return path;
    }
}

internal class UniWebViewAndroidManifest : UniWebViewAndroidXmlDocument {
    private readonly XmlElement ManifestElement;
    private readonly XmlElement ApplicationElement;

    public UniWebViewAndroidManifest(string path) : base(path) {
        ManifestElement = SelectSingleNode("/manifest") as XmlElement;
        ApplicationElement = SelectSingleNode("/manifest/application") as XmlElement;
    }

    private XmlAttribute CreateAndroidAttribute(string key, string value) {
        XmlAttribute attr = CreateAttribute("android", key, AndroidXmlNamespace);
        attr.Value = value;
        return attr;
    }

    internal XmlNode GetActivityWithLaunchIntent() {
        return
            SelectSingleNode(
                "/manifest/application/activity[intent-filter/action/@android:name='android.intent.action.MAIN' and "
                + "intent-filter/category/@android:name='android.intent.category.LAUNCHER']",
                nameSpaceManager);
    }

    internal bool SetUsesCleartextTraffic() {
        bool changed = false;
        if (ApplicationElement.GetAttribute("usesCleartextTraffic", AndroidXmlNamespace) != "true") {
            ApplicationElement.SetAttribute("usesCleartextTraffic", AndroidXmlNamespace, "true");
            changed = true;
        }
        return changed;
    }

    internal bool SetHardwareAccelerated() {
        bool changed = false;
        var activity = GetActivityWithLaunchIntent() as XmlElement;
        if (activity.GetAttribute("hardwareAccelerated", AndroidXmlNamespace) != "true") {
            activity.SetAttribute("hardwareAccelerated", AndroidXmlNamespace, "true");
            changed = true;
        }
        return changed;
    }

    internal bool AddCameraPermission() {
        bool changed = false;
        if (SelectNodes("/manifest/uses-permission[@android:name='android.permission.CAMERA']", nameSpaceManager).Count == 0) {
            var elem = CreateElement("uses-permission");
            elem.Attributes.Append(CreateAndroidAttribute("name", "android.permission.CAMERA"));
            ManifestElement.AppendChild(elem);
            changed = true;
        }
        if (SelectNodes("/manifest/uses-feature[@android:name='android.hardware.camera']", nameSpaceManager).Count == 0) {
            var elem = CreateElement("uses-feature");
            elem.Attributes.Append(CreateAndroidAttribute("name", "android.hardware.camera"));
            ManifestElement.AppendChild(elem);
            changed = true;
        }
        return changed;
    }

    internal bool AddMicrophonePermission() {
        bool changed = false;
        if (SelectNodes("/manifest/uses-permission[@android:name='android.permission.MICROPHONE']", nameSpaceManager).Count == 0) {
            var elem = CreateElement("uses-permission");
            elem.Attributes.Append(CreateAndroidAttribute("name", "android.permission.MICROPHONE"));
            ManifestElement.AppendChild(elem);
            changed = true;
        }
        if (SelectNodes("/manifest/uses-feature[@android:name='android.hardware.microphone']", nameSpaceManager).Count == 0) {
            var elem = CreateElement("uses-feature");
            elem.Attributes.Append(CreateAndroidAttribute("name", "android.hardware.microphone"));
            ManifestElement.AppendChild(elem);
            changed = true;
        }
        return changed;
    }

    internal bool AddReadExternalStoragePermission() {
        bool changed = false;
        if (SelectNodes("/manifest/uses-permission[@android:name='android.permission.READ_EXTERNAL_STORAGE']", nameSpaceManager).Count == 0) {
            var elem = CreateElement("uses-permission");
            elem.Attributes.Append(CreateAndroidAttribute("name", "android.permission.READ_EXTERNAL_STORAGE"));
            ManifestElement.AppendChild(elem);
            changed = true;
        }
        return changed;
    }

    internal bool AddWriteExternalStoragePermission() {
        bool changed = false;
        if (SelectNodes("/manifest/uses-permission[@android:name='android.permission.WRITE_EXTERNAL_STORAGE']", nameSpaceManager).Count == 0) {
            var elem = CreateElement("uses-permission");
            elem.Attributes.Append(CreateAndroidAttribute("name", "android.permission.WRITE_EXTERNAL_STORAGE"));
            ManifestElement.AppendChild(elem);
            changed = true;
        }
        return changed;
    }

    internal bool AddAccessFineLocationPermission() {
        bool changed = false;
        if (SelectNodes("/manifest/uses-permission[@android:name='android.permission.ACCESS_FINE_LOCATION']", nameSpaceManager).Count == 0) {
            var elem = CreateElement("uses-permission");
            elem.Attributes.Append(CreateAndroidAttribute("name", "android.permission.ACCESS_FINE_LOCATION"));
            ManifestElement.AppendChild(elem);
            changed = true;
        }
        return changed;
    }

    internal bool AddAuthCallbacksIntentFilter(string[] authCallbackUrls) {
        bool changed = false;
        XmlElement authActivityNode;
        if (authCallbackUrls.Length > 0) {
            var list = SelectNodes("/manifest/application/activity[@android:name='com.onevcat.uniwebview.UniWebViewAuthenticationActivity']", nameSpaceManager);
            if (list.Count == 0) {
                var created = CreateElement("activity");
                created.SetAttribute("name", AndroidXmlNamespace, "com.onevcat.uniwebview.UniWebViewAuthenticationActivity");
                created.SetAttribute("exported", AndroidXmlNamespace, "true");
                created.SetAttribute("launchMode", AndroidXmlNamespace, "singleTop");
                created.SetAttribute("configChanges", AndroidXmlNamespace, "orientation|screenSize|keyboardHidden");
                authActivityNode = created;
            } else {
                authActivityNode = list[0] as XmlElement;
            }
        } else {
            return changed;
        }

        foreach (var url in authCallbackUrls) {
            var intentFilter = CreateIntentFilter(url);
            if (intentFilter != null) {
                authActivityNode.AppendChild(intentFilter);
                changed = true;
            }
        }
        ApplicationElement.AppendChild(authActivityNode);
        return changed;
    }

    private XmlElement CreateIntentFilter(string url) {
        
        var uri = new Uri(url);
        var scheme = uri.Scheme;
        if (String.IsNullOrEmpty(scheme)) {
            Debug.LogError("<UniWebView> Auth callback url contains an empty scheme. Please check the url: " + url);
            return null;
        }

        var filter = CreateElement("intent-filter");
        
        var action = CreateElement("action");
        action.SetAttribute("name", AndroidXmlNamespace, "android.intent.action.VIEW");
        filter.AppendChild(action);
        
        var defaultCategory = CreateElement("category");
        defaultCategory.SetAttribute("name", AndroidXmlNamespace, "android.intent.category.DEFAULT");
        filter.AppendChild(defaultCategory);
        
        var browseCategory = CreateElement("category");
        browseCategory.SetAttribute("name", AndroidXmlNamespace, "android.intent.category.BROWSABLE");
        filter.AppendChild(browseCategory);
        
        var data = CreateElement("data");
        data.SetAttribute("scheme", AndroidXmlNamespace, scheme);
        if (!String.IsNullOrEmpty(uri.Host)) {
            data.SetAttribute("host", AndroidXmlNamespace, uri.Host);
        }
        if (uri.Port != -1) {
            data.SetAttribute("port", AndroidXmlNamespace, uri.Port.ToString());
        }
        if (!String.IsNullOrEmpty(uri.PathAndQuery) && uri.PathAndQuery != "/") {
            data.SetAttribute("path", AndroidXmlNamespace, uri.PathAndQuery);
        }
        
        filter.AppendChild(data);
        return filter;
    }

    // ZeyWin: Harden UniWebViewProxyActivity against the "null activity handler found!" fatal crash.
    // That transparent proxy activity resolves its handler from a process-static LinkedHashMap in
    // onCreate() (keyed by an id carried in the launch Intent) and throws a RuntimeException when the
    // lookup misses. When the OS kills the app while the proxy sits in the back stack, it later
    // re-delivers the persisted Intent to a fresh proxy instance in a new process where that map is
    // empty, crashing the app on the next cold start before Unity even loads. There is no
    // savedInstanceState guard inside the (prebuilt) .aar, so we stop the OS from ever recreating the
    // proxy instead: finishOnTaskLaunch discards a stranded instance when the task is relaunched, and
    // noHistory keeps it out of the back stack once it loses focus. excludeFromRecents is cosmetic.
    // If in-WebView file uploads ever regress, dropping only the noHistory line is the safe revert.
    internal bool HardenProxyActivity() {
        bool changed = false;
        var list = SelectNodes(
            "/manifest/application/activity[@android:name='com.onevcat.uniwebview.UniWebViewProxyActivity']",
            nameSpaceManager);

        XmlElement activity;
        if (list.Count == 0) {
            activity = CreateElement("activity");
            activity.Attributes.Append(
                CreateAndroidAttribute("name", "com.onevcat.uniwebview.UniWebViewProxyActivity"));
            ApplicationElement.AppendChild(activity);
            changed = true;
        } else {
            activity = list[0] as XmlElement;
        }

        changed = SetAndroidAttribute(activity, "finishOnTaskLaunch", "true") || changed;
        changed = SetAndroidAttribute(activity, "excludeFromRecents", "true") || changed;
        changed = SetAndroidAttribute(activity, "noHistory", "true") || changed;
        return changed;
    }

    private bool SetAndroidAttribute(XmlElement element, string key, string value) {
        if (element.GetAttribute(key, AndroidXmlNamespace) == value) {
            return false;
        }
        element.SetAttribute(key, AndroidXmlNamespace, value);
        return true;
    }
}