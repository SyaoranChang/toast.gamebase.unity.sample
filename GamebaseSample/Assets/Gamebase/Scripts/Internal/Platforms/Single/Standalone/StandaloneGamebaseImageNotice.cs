#if UNITY_EDITOR || UNITY_STANDALONE
using System;
using System.Collections.Generic;
using Toast.Gamebase.Internal.Single.Communicator;
using Toast.Gamebase.LitJson;
using UnityEngine;

namespace Toast.Gamebase.Internal.Single.Standalone
{
    public class StandaloneGamebaseImageNotice : CommonGamebaseImageNotice
    {
        private class ClickType
        {
            public const string NONE = "none";
            public const string OPEN_URL = "openUrl";
            public const string CUSTOM = "custom";
        }

        private const string TYPE_ROLLING = "ROLLING";
        private const string TYPE_POPUP = "POPUP";

        private const string ERROR_SCHEME = "cef://error";
        private const string DISMISS_SCHEME = "gamebase://dismiss";
        private const string IMAGE_NOTICE_SCHEME = "gamebase://imagenotice";

        private const string ACTION = "action";
        private const string ACTION_CLICK = "click";
        private const string ACTION_ID = "id";
        private const string ACTION_NEVER_SHOW_TODAY = "nevershowtoday";

        private const string FIXED_ROLLING_OPTION = "&orientation=landscape";

        private const string NEVER_SHOW_TODAY_STATE_KEY = "NEVER_SHOW_TODAY_STATE_KEY";
        private const string NEVER_SHOW_TODAY_ROLLING_STATE_KEY = "NEVER_SHOW_TODAY_ROLLING_STATE_KEY";

        private const string MESSAGE_CANNOT_BN_OPENED = "The image notice cannot be opened due to the 'Don't ask again today' setting.";
        private const string MESSAGE_TURNED_OFF = "The 'Don't ask again today' setting is turned off.";
        private const string MESSAGE_REMOVED_NOTICE = "The exposure has been discontinued and the ID has been removed.";
        private const string MESSAGE_NO_IMAGE_NOTICE = "There are no image notice to display.";
        private const string MESSAGE_NEXT_POPUP_TIME_MILLIS_IS_NULL = "The nextPopupTimeMillis is null.";
        private const string MESSAGE_INVALID_ID = "Invalid ID.";

        private const float SCREEN_WIDTH = 1920f;
        private const float SCREEN_HEIGHT = 1080f;

        private const float STANDARD_IMAGE_WIDTH = 600f;
        private const float STANDARD_IMAGE_HEIGHT = 450f;

        private const int MINIMUM_WEBVIEW_WIDTH = 200;
        private const int TITLE_BAR_HEIGHT = 41;

        private GamebaseCallback.ErrorDelegate closeCallback;
        private GamebaseCallback.GamebaseDelegate<string> eventCallback;
        private Color bgColor;

        private ImageNoticeResponse.ImageNotices.ImageNoticeWeb imageNotices;

        private Dictionary<string, string> neverShowTodayState;

        private ImageNoticeResponse.ImageNotices.ImageNoticeWeb.ImageNoticeInfo currentImageNotice;
        private int currentIndex = 0;

        private WebSocketRequest.RequestVO requestVO;

        public StandaloneGamebaseImageNotice()
        {
            Domain = typeof(StandaloneGamebaseImageNotice).Name;
            requestVO = ImageNoticeMessage.GetImageNoticesMessage();
        }

        public override void ShowImageNotices(GamebaseRequest.ImageNotice.Configuration configuration, int closeHandle, int eventHandle)
        {
            currentIndex = 0;

            if (configuration == null)
            {
                bgColor = new Color(0f, 0f, 0f, 0.5f);
            }
            else
            {
                bgColor = new Color(
                    configuration.colorR / 255f,
                    configuration.colorG / 255f,
                    configuration.colorB / 255f,
                    configuration.colorA / 255f);
            }

            closeCallback = GamebaseCallbackHandler.GetCallback<GamebaseCallback.ErrorDelegate>(closeHandle);
            GamebaseCallbackHandler.UnregisterCallback(closeHandle);

            eventCallback = GamebaseCallbackHandler.GetCallback<GamebaseCallback.GamebaseDelegate<string>>(eventHandle);
            GamebaseCallbackHandler.UnregisterCallback(eventHandle);

            RequestImageNoticeData();
        }

        public override void CloseImageNotices()
        {
            closeCallback = null;
            eventCallback = null;
            currentIndex = 0;
            WebviewAdapterManager.Instance.CloseWebView();
        }

        private void RequestImageNoticeData()
        {
            WebSocket.Instance.Request(requestVO, (response, error) =>
            {
                imageNotices = null;

                IsValidServerResponse(response, error, (responseError) =>
                {
                    if (Gamebase.IsSuccess(responseError))
                    {
                        if (HasImageNotice() == false)
                        {
                            GamebaseLog.Debug(MESSAGE_NO_IMAGE_NOTICE, this);
                            closeCallback(null);
                            return;
                        }

                        if (IsPopupType())
                        {
                            InitNeverShowTodayState(NEVER_SHOW_TODAY_STATE_KEY);
                            ShowNextPopup();
                        }
                        else
                        {
                            InitNeverShowTodayState(NEVER_SHOW_TODAY_ROLLING_STATE_KEY);
                            ShowRolling();
                        }
                    }
                    else
                    {
                        if (closeCallback != null)
                        {
                            closeCallback(responseError);
                        }
                    }
                });
            });
        }

        private void ShowNextPopup()
        {
            if (CheckNextImageExists() == false)
            {
                if (closeCallback != null)
                {
                    closeCallback(null);
                }

                return;
            }

            currentImageNotice = imageNotices.pageList[currentIndex];
            currentIndex++;

            if (CheckNeverShowToday(currentImageNotice.imageNoticeId) == true)
            {
                GamebaseLog.Debug(
                    string.Format("{0} id:{1}", MESSAGE_CANNOT_BN_OPENED, currentImageNotice.imageNoticeId),
                    this);

                ShowNextPopup();
                return;
            }

            ShowWebview(string.Concat(imageNotices.address, currentImageNotice.path));
        }

        private void ShowRolling()
        {
            if (CheckNeverShowToday(imageNotices.rollingImageNoticeId) == true)
            {
                GamebaseLog.Debug(
                    string.Format("{0} id:{1}", MESSAGE_CANNOT_BN_OPENED, imageNotices.rollingImageNoticeId),
                    this);

                return;
            }

            ShowWebview(string.Concat(imageNotices.address, FIXED_ROLLING_OPTION));
        }

        private void ShowWebview(string url)
        {
            bool hasAdapter = WebviewAdapterManager.Instance.CreateWebviewAdapter("standalonewebviewadapter");
            if (hasAdapter == false)
            {
                if (closeCallback != null)
                {
                    closeCallback(new GamebaseError(GamebaseErrorCode.NOT_SUPPORTED, Domain, GamebaseStrings.WEBVIEW_ADAPTER_NOT_FOUND));
                }

                return;
            }

            var webviewRect = GetWebViewRect(TITLE_BAR_HEIGHT);

            if (IsPopupType())
            {
                if (webviewRect.width < MINIMUM_WEBVIEW_WIDTH)
                {
                    webviewRect.x -= (int)((MINIMUM_WEBVIEW_WIDTH - webviewRect.width) / 2);
                    webviewRect.width = MINIMUM_WEBVIEW_WIDTH;
                    url = string.Concat(url, "&orientation=landscape");
                }
            }

            WebviewAdapterManager.Instance.ShowWebView(
               url,
               null,
               (error) =>
               {
                   if (IsPopupType())
                   {
                       ShowNextPopup();
                   }
               },
               new List<string>()
               {
                    DISMISS_SCHEME,
                    IMAGE_NOTICE_SCHEME
                },
               (scheme, error) =>
               {
                   WebviewAdapterManager.SchemeInfo schemeInfo = WebviewAdapterManager.Instance.ConvertURLToSchemeInfo(scheme);
                   GamebaseLog.Debug(string.Format("scheme:{0}, data:{1}", scheme, JsonMapper.ToJson(schemeInfo.parameterDictionary)), this);

                   if (schemeInfo.scheme == ERROR_SCHEME)
                   {
                       WebviewAdapterManager.Instance.CloseWebView();
                       return;  
                   }

                   switch (schemeInfo.scheme)
                   {
                       case DISMISS_SCHEME:
                           {
                               WebviewAdapterManager.Instance.CloseWebView();
                               break;
                           }
                       case IMAGE_NOTICE_SCHEME:
                           {
                               switch (schemeInfo.parameterDictionary[ACTION])
                               {
                                   case ACTION_CLICK:
                                       {
                                           if (IsPopupType() == false)
                                           {
                                               if (string.IsNullOrEmpty(schemeInfo.parameterDictionary[ACTION_ID]))
                                               {
                                                   GamebaseLog.Warn(MESSAGE_INVALID_ID, this);
                                                   return;
                                               }

                                               var imageNoticeId = (long)Convert.ToDouble(schemeInfo.parameterDictionary[ACTION_ID]);
                                               currentImageNotice = imageNotices.pageList.Find(info => info.imageNoticeId == imageNoticeId);
                                           }

                                           OnActionClick(currentImageNotice.clickType);
                                           break;
                                       }
                                   case ACTION_NEVER_SHOW_TODAY:
                                       {
                                           if (IsPopupType())
                                           {
                                               OnActionNeverShowToday(currentImageNotice.imageNoticeId);
                                           }
                                           else
                                           {
                                               OnActionNeverShowToday(imageNotices.rollingImageNoticeId);
                                           }

                                           WebviewAdapterManager.Instance.CloseWebView();
                                           break;
                                       }
                               }
                               break;
                           }
                   }
               },
               () =>
               {
                   WebviewAdapterManager.Instance.SetBgColor(bgColor);
                   WebviewAdapterManager.Instance.SetWebViewRect(TITLE_BAR_HEIGHT, webviewRect);
                   WebviewAdapterManager.Instance.SetTitleBarColor(bgColor);
                   WebviewAdapterManager.Instance.SetTitleBarButton(false, null, null);
                   WebviewAdapterManager.Instance.SetTitleVisible(false);
               });
        }

        private void OnActionClick(string clickType)
        {
            switch (clickType)
            {
                case ClickType.NONE:
                    {
                        break;
                    }
                case ClickType.OPEN_URL:
                    {
                        Application.OpenURL(UnityCompatibility.WebRequest.UnEscapeURL(currentImageNotice.clickScheme));
                        break;
                    }
                case ClickType.CUSTOM:
                    {
                        if (eventCallback != null)
                        {
                            eventCallback(UnityCompatibility.WebRequest.UnEscapeURL(currentImageNotice.clickScheme), null);
                        }
                        break;
                    }
            }
        }

        private void OnActionNeverShowToday(long imageNoticeId)
        {
            var strImageNoticeId = imageNoticeId.ToString();

            if (imageNotices.nextPopupTimeMillis == -1)
            {
                GamebaseLog.Warn(MESSAGE_NEXT_POPUP_TIME_MILLIS_IS_NULL, this);
                return;
            }

            var strNow = imageNotices.nextPopupTimeMillis.ToString();

            if (neverShowTodayState.ContainsKey(strImageNoticeId))
            {
                neverShowTodayState[strImageNoticeId] = strNow;
            }
            else
            {
                neverShowTodayState.Add(strImageNoticeId, strNow);
            }

            if (IsPopupType())
            {
                PlayerPrefs.SetString(NEVER_SHOW_TODAY_STATE_KEY, JsonMapper.ToJson(neverShowTodayState));
            }
            else
            {
                PlayerPrefs.SetString(NEVER_SHOW_TODAY_ROLLING_STATE_KEY, JsonMapper.ToJson(neverShowTodayState));
            }
        }

        private void InitNeverShowTodayState(string key)
        {
            var localData = PlayerPrefs.GetString(key);

            if (string.IsNullOrEmpty(localData))
            {
                neverShowTodayState = new Dictionary<string, string>();
            }
            else
            {
                neverShowTodayState = JsonMapper.ToObject<Dictionary<string, string>>(localData);
                RemoveUnusedId();
            }
        }

        private void RemoveUnusedId()
        {
            if (neverShowTodayState.Count == 0)
            {
                return;
            }

            if (IsPopupType())
            {
                List<string> removeIds = new List<string>();

                foreach (var id in neverShowTodayState.Keys)
                {
                    if (imageNotices.pageList.Find(info => info.imageNoticeId.ToString().Equals(id)) == null)
                    {
                        // unused id
                        GamebaseLog.Debug(string.Format(
                            "{0}, id:{1}",
                            MESSAGE_REMOVED_NOTICE,
                            id), this);                        

                        removeIds.Add(id);
                    }
                    else
                    {
                        if (IsExpired(neverShowTodayState[id]))
                        {
                            GamebaseLog.Debug(string.Format(
                                "{0}, id:{1}",
                                MESSAGE_TURNED_OFF,
                                id), this);

                            removeIds.Add(id);
                        }
                    }
                }

                if (removeIds.Count == 0)
                {
                    return;
                }

                // remove id
                for (var i = 0; i < removeIds.Count; i++)
                {
                    neverShowTodayState.Remove(removeIds[i]);
                }

                PlayerPrefs.SetString(NEVER_SHOW_TODAY_STATE_KEY, JsonMapper.ToJson(neverShowTodayState));
            }
            else
            {
                var strImageNoticeId = imageNotices.rollingImageNoticeId.ToString();

                if (neverShowTodayState.ContainsKey(strImageNoticeId))
                {
                    if (IsExpired(neverShowTodayState[strImageNoticeId]))
                    {
                        GamebaseLog.Debug(string.Format(
                            "{0}, id:{1}",
                            MESSAGE_TURNED_OFF,
                            strImageNoticeId), this);
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    // unused id
                    GamebaseLog.Debug(string.Format(
                        "{0}, id:{1}",
                        MESSAGE_REMOVED_NOTICE,
                        strImageNoticeId), this);
                }

                neverShowTodayState.Clear();
                PlayerPrefs.DeleteKey(NEVER_SHOW_TODAY_ROLLING_STATE_KEY);

            }
        }

        private bool IsExpired(string imageNoticeId)
        {
            var savedTime = new DateTime(1970, 1, 1).AddMilliseconds(long.Parse(imageNoticeId)).ToLocalTime();
            var now = DateTime.UtcNow.ToLocalTime();

            return now >= savedTime;
        }

        private bool CheckNeverShowToday(long imageNoticeId)
        {
            return neverShowTodayState.ContainsKey(imageNoticeId.ToString());
        }

        private void IsValidServerResponse(string response, GamebaseError serverError, GamebaseCallback.ErrorDelegate callback)
        {
            // 1. check server error
            if (Gamebase.IsSuccess(serverError) == false)
            {
                callback(serverError);
                return;
            }

            // 2. check responsse error
            if (string.IsNullOrEmpty(response))
            {
                callback(new GamebaseError(GamebaseErrorCode.SERVER_UNKNOWN_ERROR, Domain));
                return;
            }

            var vo = JsonMapper.ToObject<ImageNoticeResponse.ImageNotices>(response);

            if (vo.header.isSuccessful)
            {
                imageNotices = vo.imageNoticeWeb;

                // 3. has image notice
                if (HasImageNotice() == false)
                {
                    callback(null);
                    return;
                }

                // 4. check popup type.
                if (IsVaildType(imageNotices.type))
                {
                    // 5. check rollingImageNoticeId.
                    if (IsValidRollingImageNoticeId())
                    {
                        callback(null);
                    }
                    else
                    {   
                        callback(new GamebaseError(GamebaseErrorCode.SERVER_INVALID_RESPONSE, Domain));
                    }
                }
                else
                {
                    callback(new GamebaseError(GamebaseErrorCode.SERVER_INVALID_RESPONSE, Domain));
                }
            }
            else
            {
                callback(GamebaseErrorUtil.CreateGamebaseErrorByServerErrorCode(requestVO.transactionId, requestVO.apiId, vo.header, Domain));
            }
        }

        private bool IsPopupType()
        {
            return imageNotices.type.Equals(TYPE_POPUP);
        }

        private bool HasImageNotice()
        {
            return imageNotices.hasImageNotice;
        }

        private bool IsVaildType(string type)
        {
            if (string.IsNullOrEmpty(type))
            {
                return false;
            }

            if (type.Equals(TYPE_POPUP) || type.Equals(TYPE_ROLLING))
            {
                return true;
            }
            else
            {
                return false;
            }

        }

        private bool IsValidRollingImageNoticeId()
        {
            if (IsPopupType() == false)
            {
                if (imageNotices.rollingImageNoticeId == -1)
                {
                    return false;
                }
            }

            return true;
        }

        private bool CheckNextImageExists()
        {
            if (imageNotices == null || imageNotices.pageList == null)
            {
                return false;
            }

            if (currentIndex >= imageNotices.pageList.Count)
            {
                return false;
            }

            return true;
        }

        #region Get size and position
        private Rect GetWebViewRect(int titleBarHeight)
        {
            var scale = GetScale();

            Vector2 imageSize;

            if (IsPopupType())
            {
                // Formula for calculating image size: POPUP
                // width: image width *  scale
                // height: image height * scale + footer height 
                imageSize = new Vector2(currentImageNotice.imageInfo.width * scale, currentImageNotice.imageInfo.height * scale + imageNotices.footerHeight);
            }
            else
            {
                // Formula for calculating image size: ROLLING
                // width(fixed): ROLLING_IMAGE_WIDTH *  scale
                // height(fixed): ROLLING_IMAGE_HEIGHT * scale 
                imageSize = new Vector2(STANDARD_IMAGE_WIDTH * scale, STANDARD_IMAGE_HEIGHT * scale);
            }

            imageSize.y += titleBarHeight;

            var size = GetWebViewSize(imageSize);
            var position = GetWebViewPosition(size);

            return new Rect(position, size);
        }

        private float GetScale()
        {
            Vector2 scale = new Vector2(Screen.width / SCREEN_WIDTH, Screen.height / SCREEN_HEIGHT);
            return Math.Min(scale.x, scale.y);
        }

        private Vector2 GetWebViewSize(Vector2 imageSize)
        {
            float ratio = 1;

            if (imageSize.x > STANDARD_IMAGE_WIDTH)
            {
                ratio = STANDARD_IMAGE_WIDTH / imageSize.x;
            }
            else if (imageSize.y > STANDARD_IMAGE_HEIGHT)
            {
                ratio = STANDARD_IMAGE_HEIGHT / imageSize.y;
            }

            imageSize *= ratio;

            return new Vector2((int)imageSize.x, (int)imageSize.y);
        }

        private Vector2 GetWebViewPosition(Vector2 imageSize)
        {
            return new Vector2(
                (int)((Screen.width - imageSize.x) * 0.5f),
                (int)((Screen.height - imageSize.y) * 0.5f));
        }
        #endregion
    }
}
#endif
