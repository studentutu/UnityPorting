﻿using System;
using System.Threading.Tasks;
using Facebook;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace MyPlugin.Facebook
{
    public delegate void RequestStateChangedHandler(FacebookRequest request, NavigationState state);

    public sealed class Facebook
    {
        private static Facebook _instance;
        private static readonly object _sync = new object();
        public static Facebook Instance
        {
            get
            {
                lock (_sync)
                {
                    if (_instance == null)
                        _instance = new Facebook();
                }
                return _instance;
            }
        }

        const string PermissionsString = "user_about_me";
#if DEBUG || QA
        const string FBAppId = "1375004912734033"; // MQ: Temp ID "MQ Game Test"
#else
        const string FBAppId = "287090944731453"; // WOF Production App ID
#endif
        const string RedirectUrl = "https://www.facebook.com/connect/login_success.html";
        const string ATKey = "ATK";

        private FacebookClient _fb;
        private WebView _web;
        private Task<NavigationState> _callbackHook;

        public event RequestStateChangedHandler StateChanged;

        public FacebookRequest CurrentRequest { get; private set; }
        public NavigationState LatestState { get; private set; }
        public bool IsLoggedIn { get { return _fb != null && !string.IsNullOrWhiteSpace(_fb.AccessToken); } }
        public string AccessToken { get { return _fb.AccessToken; } }

        private Facebook()
        {
            _fb = new FacebookClient();
            _fb.AppId = FBAppId;
        }

        public void Initialise(WebView webView)
        {
            if (_web != null)
            {
                _web.LoadCompleted -= NavigationComplete;
                _web.NavigationFailed -= NavigationFailed;
            }

            _fb.AccessToken = EncryptedStore.LoadSetting(ATKey);

            _web = webView;

            if (_web != null)
            {
                _web.LoadCompleted += NavigationComplete;
                _web.NavigationFailed += NavigationFailed;
            }
        }

        public Task<NavigationState> LoginAsync()
        {
            CurrentRequest = FacebookRequest.Login;
            if (_callbackHook != null || _fb == null) return Task.FromResult<NavigationState>(NavigationState.Error);
            if (IsLoggedIn) return Task.FromResult<NavigationState>(NavigationState.Done);

            // Perform a proper login
            var uri = _fb.GetLoginUrl(new
            {
                redirect_uri = RedirectUrl,
                scope = PermissionsString,
                display = "popup",
                response_type = "token"
            });
            CreateTask();
            ChangeNavigationState(NavigationState.Navigating);
            _web.Navigate(uri);

            return _callbackHook;
        }

        public Task<NavigationState> LogoutAsync()
        {
            CurrentRequest = FacebookRequest.Logout;
            if (_callbackHook != null || _fb == null) return Task.FromResult<NavigationState>(NavigationState.Error);
            if (!IsLoggedIn) return Task.FromResult<NavigationState>(NavigationState.Done);

            var uri = _fb.GetLogoutUrl(new
                {
                    access_token = _fb.AccessToken,
                    next = RedirectUrl
                });
            CreateTask();
            ChangeNavigationState(NavigationState.Navigating);
            _web.Navigate(uri);

            return _callbackHook;
        }

        public Task<NavigationState> InviteFriendsAsync(string friendName)
        {
            CurrentRequest = FacebookRequest.InviteRequest;
            if (_callbackHook != null || !IsLoggedIn) return Task.FromResult<NavigationState>(NavigationState.Error);

            var uri = _fb.GetDialogUrl("apprequests", new
            {
                to = friendName,
                redirect_uri = RedirectUrl,
                message = "Let's spin together in Wheel of Fortune",
                display = "popup"
            });
            CreateTask();
            ChangeNavigationState(NavigationState.Navigating);
            _web.Navigate(uri);

            return _callbackHook;
        }

        public void Cancel()
        {
            if (_callbackHook != null)
            {
                ChangeNavigationState(NavigationState.Error);
                _web.NavigateToString("");
            }
        }

        private void NavigationComplete(object sender, NavigationEventArgs e)
        {
            if (LatestState == NavigationState.Error || LatestState == NavigationState.Done) return;

            switch (CurrentRequest)
            {
                case FacebookRequest.Login:
                    {
                        FacebookOAuthResult result;
                        // Check if this is a login result, otherwise we're waiting for input
                        if (_fb.TryParseOAuthCallbackUrl(e.Uri, out result))
                        {
                            if (result.IsSuccess)
                            {
                                // Successful login, store the access token
                                _fb.AccessToken = result.AccessToken;
                                EncryptedStore.SaveSetting(ATKey, _fb.AccessToken);
                                ChangeNavigationState(NavigationState.Done);
                            }
                            else
                            {
                                // Login was rejected or cancelled, error out
                                ChangeNavigationState(NavigationState.Error);
                            }
                        }
                        else
                        {
                            ChangeNavigationState(NavigationState.UserInput);
                        }
                    }
                    break;

                case FacebookRequest.Logout:
                    _fb.AccessToken = null;
                    EncryptedStore.Remove(ATKey);
                    ChangeNavigationState(NavigationState.Done);
                    break;

                case FacebookRequest.InviteRequest:
                    {
                        var response = _fb.ParseDialogCallbackUrl(e.Uri) as dynamic;
                        if (response.request != null)
                            ChangeNavigationState(NavigationState.Done);
                        else if (e.Uri.PathAndQuery.Contains("/login_success"))
                            ChangeNavigationState(NavigationState.Error);
                    }
                    break;
            }
        }

        private void NavigationFailed(object sender, WebViewNavigationFailedEventArgs e)
        {
            ChangeNavigationState(NavigationState.Error);
        }

        private void ChangeNavigationState(NavigationState state)
        {
            if (state == LatestState) return;

            LatestState = state;
            if (StateChanged != null)
                StateChanged(CurrentRequest, LatestState);

            if (_callbackHook != null && (state == NavigationState.Done || state == NavigationState.Error))
            {
                _callbackHook.Start();
                _callbackHook = null;
            }
        }

        private void CreateTask()
        {
            _callbackHook = new Task<NavigationState>(() => LatestState);
        }
    }
}