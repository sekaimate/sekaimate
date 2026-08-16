using System;
using Basis.BasisUI;
using Basis.Scripts.Device_Management;
using UnityEngine;

public class BasisOpenMenuForcefully : MonoBehaviour
{
    public bool OpenServerMenu = true;
    public string ProviderTitleKey = "menu.provider.servers";
    public void Start()
    {
        if(BasisDeviceManagement.OnInitializationComplete)
        {
            OpenMenu();
        }
        else
        {
            BasisDeviceManagement.OnInitializationCompleted += OpenMenu;
        }
    }
    public void OnDestroy()
    {
        BasisDeviceManagement.OnInitializationCompleted -= OpenMenu;
    }
    public void OpenMenu()
    {
        if (IsWebMeetingLaunch())
        {
            return;
        }

        BasisMainMenu.Open();
        if (OpenServerMenu)
        {
            BasisMainMenu.OpenWithProvider(BasisLocalization.Get(ProviderTitleKey));
        }
        else
        {
            BasisMainMenu.Open();
        }
    }

    private static bool IsWebMeetingLaunch()
    {
        if (!Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out Uri page))
        {
            return false;
        }

        string query = page.Query.TrimStart('?');
        foreach (string part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = part.IndexOf('=');
            string key = separator >= 0 ? part[..separator] : part;
            string value = separator >= 0 ? part[(separator + 1)..] : string.Empty;
            if (string.Equals(key, "basisMeeting", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Uri.UnescapeDataString(value), "1", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
