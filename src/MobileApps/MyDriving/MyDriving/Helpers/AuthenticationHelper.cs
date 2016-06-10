using Microsoft.WindowsAzure.MobileServices;
using MyDriving.AzureClient;
using MyDriving.DataStore.Abstractions;
using MyDriving.Interfaces;
using MyDriving.Utils;
using System;
using System.Threading.Tasks;

namespace MyDriving.Helpers
{
    public class AuthenticationHelper
    {
        public static bool firstSync = true;
        public static async Task<bool> Login()
        {
            if (!Plugin.Connectivity.CrossConnectivity.Current.IsConnected)
            {
                Acr.UserDialogs.UserDialogs.Instance.Alert("Ensure you have internet connection to login.",
                    "No Connection", "OK");

                return false;
            }

            var authentication = ServiceLocator.Instance.Resolve<IAuthentication>();
            var client = ServiceLocator.Instance.Resolve<IAzureClient>()?.Client as MobileServiceClient;
            if (client == null)
            {
                throw new InvalidOperationException(
                    "Make sure to set the ServiceLocator has an instance of IAzureClient");
            }
            if (authentication == null)
            {
                throw new InvalidOperationException("Make sure to set the ServiceLocator has an instance of IAuthentication");
            }


            var accountType = MobileServiceAuthenticationProvider.MicrosoftAccount;
            switch (Settings.Current.LoginAccount)
            {
                case LoginAccount.Facebook:
                    accountType = MobileServiceAuthenticationProvider.Facebook;
                    break;
                case LoginAccount.Twitter:
                    accountType = MobileServiceAuthenticationProvider.Twitter;
                    break;
            }
            try
            {
                var user = await authentication.LoginAsync(client, accountType);

                if (user != null)
                {
                    Settings.Current.AzureMobileUserId = user.UserId;
                    Settings.Current.AuthToken = user.MobileServiceAuthenticationToken;
                    return true;
                }
                else
                    return false;
            }
            catch (System.Exception e)
            {
                Logger.Instance.Report(e);
            }

            return false;
        }

        public static async Task<bool> CheckTokenExpired()
        {
            if (Settings.Current.TokenExpired)
            {
                if (await Login())
                {
                    Settings.Current.TokenExpired = false;
                }
            }

            if (firstSync)
            {
                await DoFirstSync();
            }
            return !Settings.Current.TokenExpired;
        }

        //when launching the app the token may be expired but Settings.Current.TokenExpired is false
        public static async Task DoFirstSync()
        {
            ITripStore tripStore = ServiceLocator.Instance.Resolve<ITripStore>();
            if (tripStore != null)
            {
                await tripStore.SyncAsync();
                if (Settings.Current.TokenExpired)
                {
                    if (await Login())
                    {
                        Settings.Current.TokenExpired = false;

                        //sync again
                        await tripStore.SyncAsync();
                    }

                }
                firstSync = false;
            }
        }
    }
}
