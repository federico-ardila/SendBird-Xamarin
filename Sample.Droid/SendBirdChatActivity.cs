using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

using Android.Content;
using Android.Database;
using Android.Graphics;

using Android.Graphics.Drawables;
using Android.OS;
using Android.Runtime;
using Android.Support.V4.App;
using Android.Support.V4.Util;
using Android.Text;
using Android.Views;
using Android.Widget;

using SendBird;
using SendBird.SBJson;

using Sample.Droid;

namespace SendBirdSample.Droid
{
	[Android.App.Activity (Theme = "@android:style/Theme.DeviceDefault.Light.NoActionBar", Label = "Chat")]
	public class SendBirdChatActivity : FragmentActivity
	{
		private static SynchronizationContext mSyncContext;
		private static ImageUtils.MemoryLimitedLruCache mMemoryCache;

		private const int REQUEST_CHANNEL_LIST = 100;

		private SendBirdChatFragment mSendBirdChatFragment;
		private SendBirdChatAdapter mSendBirdChatAdapter;

		private MessageListQuery mMessageListQuery;

		private ImageButton mBtnClose;
		private ImageButton mBtnSettings;
		private TextView mTxtChannelUrl;
		private View mTopBarContainer;
		private View mSettingsContainer;
		private Button mBtnLeave;
		private string mChannelUrl;
		private bool mDoNotDisconnect;

        public const string HANDLER_ID = "DEFAULT_EVENT_HANDLER";

		public static Bundle MakeSendBirdArgs (string appId, string uuid, string userName, string channelUrl, string accessToken)
		{
			Bundle args = new Bundle ();
			args.PutString ("appId", appId);
			args.PutString ("uuid", uuid);
            args.PutString("accessToken", accessToken);
            args.PutString ("userName", userName);
			args.PutString ("channelUrl", channelUrl);
			return args;
		}

		protected override void OnDestroy ()
		{
			base.OnDestroy ();
			if (!mDoNotDisconnect) {
				SendBirdClient.Disconnect (()=> {
                    //Log something for example
                });
			}
		}

		public override void Finish ()
		{
			base.Finish ();
		}

		private void InitFragment (Bundle savedInstanceState)
		{
            mChannelUrl = Intent.Extras.GetString("channelUrl");
            mSendBirdChatFragment = new SendBirdChatFragment (mChannelUrl);

			mSendBirdChatAdapter = new SendBirdChatAdapter (this);
			mSendBirdChatFragment.mAdapter = mSendBirdChatAdapter;
			mSendBirdChatFragment.OnChannelListClicked += (sender, e) => {
				var intent = new Intent (this, typeof(SendBirdChannelListActivity));
				intent.PutExtras (this.Intent.Extras);
				StartActivityForResult (intent, REQUEST_CHANNEL_LIST);
			};

			if (savedInstanceState == null) {
				SupportFragmentManager.BeginTransaction ().Replace (Resource.Id.fragment_container, mSendBirdChatFragment).Commit (); // v4 fragment
			}
		}

		private void InitUIComponents ()
		{
			mTopBarContainer = FindViewById (Resource.Id.top_bar_container);
			mTxtChannelUrl = FindViewById (Resource.Id.txt_channel_url) as TextView;

			mSettingsContainer = FindViewById (Resource.Id.settings_container);
			mSettingsContainer.Visibility = ViewStates.Gone;

			mBtnLeave = FindViewById (Resource.Id.btn_leave) as Button;
			mBtnLeave.Click += async (sender, e) => {
				mSettingsContainer.Visibility = ViewStates.Gone;
                await ExitChannelAsync();
                Finish();
			};

			mBtnClose = FindViewById (Resource.Id.btn_close) as ImageButton;
			mBtnClose.Click += (object sender, EventArgs e) => {
				Finish ();
			}; 

			mBtnSettings = FindViewById (Resource.Id.btn_settings) as ImageButton;
			mBtnSettings.Click += (sender, e) => {
				if(mSettingsContainer.Visibility != ViewStates.Visible) {
					mSettingsContainer.Visibility = ViewStates.Visible;
				} else {
					mSettingsContainer.Visibility = ViewStates.Gone;
				}
			};
			ResizeMenubar ();
		}

        private void ResizeMenubar()
		{
			ViewGroup.LayoutParams lp = mTopBarContainer.LayoutParameters;
			if(Resources.Configuration.Orientation == Android.Content.Res.Orientation.Landscape) {
				lp.Height = (int) (28 * Resources.DisplayMetrics.Density);
			} else {
				lp.Height = (int) (48 * Resources.DisplayMetrics.Density);
			}
			mTopBarContainer.LayoutParameters = lp;
		}

		public override void OnConfigurationChanged(Android.Content.Res.Configuration newConfig)
		{
			base.OnConfigurationChanged (newConfig);
			ResizeMenubar ();
		}

		private async Task InitSendBirdAsync (Bundle extras)
		{
			string appId = extras.GetString ("appId");
			string uuid = extras.GetString ("uuid");
			string userName = extras.GetString ("userName");
            string accessToken = extras.GetString("accessToken");
			mChannelUrl = extras.GetString ("channelUrl");

			mSyncContext = SynchronizationContext.Current; // required for ui update


            #region ChannelHandler
            SendBirdClient.ChannelHandler ch = new SendBirdClient.ChannelHandler();
            ch.OnMessageReceived = OnMessageReceived;
            ch.OnMessageDeleted = OnMessageDeleted;
            #endregion

            try
            {
                SendBirdClient.Init(appId);
                await ConnectAsync(uuid, accessToken);
                mTxtChannelUrl.Text = "#" + mChannelUrl;
                mSendBirdChatAdapter.NotifyDataSetChanged();
                await JoinChatAsync();
            }catch(Exception e)
            {
                Console.WriteLine("Error Connection - " + e.Message);
            }

			SendBirdClient.AddChannelHandler(HANDLER_ID, ch);
		}

        private Task<User> ConnectAsync(string userId, string accessToken)
        {
            var t = new TaskCompletionSource<User>();
            SendBirdClient.Connect(userId, accessToken, (user, e) =>
            {
                if (e != null)
                {
                    t.TrySetException(e);
                    return;
                }
                t.TrySetResult(user);
            }); 
            return t.Task;
        }


        private Task<OpenChannel> GetChannelByUrlAsync(string channelUrl)
        {
            var t = new TaskCompletionSource<OpenChannel>();
            OpenChannel.GetChannel(channelUrl, (channel, e) =>
            {
                if (e != null)
                {
                    t.TrySetException(e);
                    return;
                }
                t.TrySetResult(channel);
            });

            return t.Task;
        }
        private OpenChannel channel;
        private async Task<OpenChannel> GetChannelAsync()
        {
            if (channel == null)
            {
                channel = await GetChannelByUrlAsync(mChannelUrl);
            }

            return channel;
        }
        private Task<List<BaseMessage>> GetPreviousMessagesAsync(BaseChannel channel,int limit)
        {
            var t = new TaskCompletionSource<List<BaseMessage>>();
            var q = channel.CreatePreviousMessageListQuery();
            q.Load(limit, false, (messages, e) =>
             {
                 if (e != null)
                 {
                     t.TrySetException(e);
                     return;
                 }
                 t.TrySetResult(messages);
             });
            return t.Task;
        }
        private async Task<List<BaseMessage>> GetPreviousMessagesAsync(int limit)
        {
            var channel = await GetChannelAsync();
            return await GetPreviousMessagesAsync(channel, limit);
        }
        private Task EnterChannelAsync(OpenChannel channel)
        {
            var t = new TaskCompletionSource<bool>();
            channel.Enter(e =>
            {
                if (e != null)
                {
                    t.SetException(e);
                    return;
                }
                t.SetResult(true);
            });
            return t.Task;
        }
        private async Task EnterChannelAsync()
        {
            var channel = await GetChannelAsync();
            await EnterChannelAsync(channel);
        }

        private Task ExitChannelAsync(OpenChannel channel)
        {
            var t = new TaskCompletionSource<bool>();
            channel.Exit(e =>
            {
                if (e != null)
                {
                    t.SetException(e);
                    return;
                }
                t.SetResult(true);
            });
            return t.Task;
        }
        private async Task ExitChannelAsync()
        {
            var channel = await GetChannelAsync();
            await ExitChannelAsync(channel);
        }


        private void  OnMessageReceived(BaseChannel BaseChannel, BaseMessage baseMessage)
        {
            mSendBirdChatAdapter.AddMessage(baseMessage);
            mSyncContext.Post(delegate
            {
                mSendBirdChatAdapter.NotifyDataSetChanged();
            }, null);
        }

        private void OnMessageDeleted(BaseChannel baseChannel , long messageId )
        {
            mSendBirdChatAdapter.DeleteMessage(baseChannel, messageId);
            mSyncContext.Post(delegate
            {
                mSendBirdChatAdapter.NotifyDataSetChanged();
            }, null);
        }

		protected override void OnActivityResult (int requestCode, Android.App.Result resultCode, Intent data)
		{
			base.OnActivityResult (requestCode, resultCode, data);

			if (requestCode == REQUEST_CHANNEL_LIST) {
				if (resultCode == Android.App.Result.Ok && data != null) {
					mChannelUrl = data.GetStringExtra ("channelUrl");

					mSendBirdChatAdapter.Clear ();
					mSendBirdChatAdapter.NotifyDataSetChanged ();

                    JoinChatAsync();

                }
			}
		}

		protected override void OnCreate (Bundle savedInstanceState)
		{
			base.OnCreate (savedInstanceState);

			// Set our view from the "main" layout resource
			SetContentView (Resource.Layout.SendBirdActivityChat);
			this.Window.SetSoftInputMode (SoftInput.StateAlwaysHidden);

			InitFragment (savedInstanceState);
			InitUIComponents ();
			InitSendBirdAsync (this.Intent.Extras);
            
		}

        private async Task JoinChatAsync()
        {
            try
            {
                var messages = await GetPreviousMessagesAsync(50);
                mSendBirdChatAdapter.AddPreviousMessages(messages);
                mSendBirdChatAdapter.NotifyDataSetChanged();
                mSendBirdChatFragment.mListView.SetSelection(mSendBirdChatAdapter.Count);
                await EnterChannelAsync();
                
            }
            catch(Exception e)
            {
                Console.WriteLine("Error joining chat - " + e.Message);
            }


        }

		public class SendBirdChatFragment : Fragment
		{
			private const int REQUEST_PICK_IMAGE = 100;

			public ListView mListView;
			public SendBirdChatAdapter mAdapter;
			public EditText mEtxtMessage;
			public Button mBtnSend;
			public ImageButton mBtnChannel;
			public ImageButton mBtnUpload;
			public ProgressBar mProgressBtnUpload;
            private readonly string _channelUrl;

			public delegate void OnChannelListClickedEvent (object sender, EventArgs e);

			public OnChannelListClickedEvent OnChannelListClicked;

			public SendBirdChatFragment (string channelUrl)
			{
                _channelUrl = channelUrl;
            }

                        private OpenChannel _channel;
            private Task<OpenChannel> GetChannelTask()
            {
                if(_channel != null)
                {
                    return Task.FromResult<OpenChannel>(_channel);
                }
                

                var t = new TaskCompletionSource<OpenChannel>();
                OpenChannel.GetChannel(_channelUrl, (channel, e) =>
                {
                    if (e != null)
                    {
                        t.TrySetException(e);
                        return;
                    }
                    _channel = channel;
                    t.TrySetResult(channel);
                });

                return t.Task;
            }
            private async Task<OpenChannel> GetChannelAsync()
            {
                return await GetChannelTask();
            }
			

			public override View OnCreateView (LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
			{
				View rootView = inflater.Inflate (Resource.Layout.SendBirdFragmentChat, container, false);
				InitUIComponents (rootView);
				return rootView;
			}

			private void InitUIComponents (View rootView)
			{
				mListView = rootView.FindViewById (Resource.Id.list_view) as ListView;
				TurnOffListViewDecoration (mListView);
				mListView.Adapter = mAdapter;

				mBtnChannel = rootView.FindViewById (Resource.Id.btn_channel) as ImageButton;
				mBtnChannel.Click += (sender, e) => {
					if (OnChannelListClicked != null) {
						OnChannelListClicked (this, new EventArgs ());
					}
				};
				mBtnSend = rootView.FindViewById (Resource.Id.btn_send) as Button;
				mBtnUpload = rootView.FindViewById (Resource.Id.btn_upload) as ImageButton;
				mBtnUpload.Click += delegate {
					Intent intent = new Intent();
					intent.SetType("image/*");
					intent.SetAction(Intent.ActionGetContent);
					StartActivityForResult(Intent.CreateChooser(intent, "Select Picture"), REQUEST_PICK_IMAGE);
				};
				mProgressBtnUpload = rootView.FindViewById (Resource.Id.progress_btn_upload) as ProgressBar;
				mEtxtMessage = rootView.FindViewById (Resource.Id.etxt_message) as EditText;
				mEtxtMessage.KeyPress += async (object sender, View.KeyEventArgs e) => {
					if (e.KeyCode == Keycode.Enter) {
						if (e.Event.Action == KeyEventActions.Down) {
                            await SendAsync();
							e.Handled = true;
						}
					} else {
						e.Handled = false;
					}
				};

				mBtnSend.Enabled = false;
				mBtnSend.Click += async (object sender, EventArgs e) => {
					await SendAsync ();
				};

				mEtxtMessage.AfterTextChanged += (object sender, AfterTextChangedEventArgs e) => {
					mBtnSend.Enabled = true;
				};
				mListView.Touch += (object sender, View.TouchEventArgs e) => {
					Helper.HideKeyboard (this.Activity);
					e.Handled = false;
				};
				mListView.ScrollStateChanged += (s, args) => {
					if(args.ScrollState == ScrollState.Idle) {
						if(args.View.FirstVisiblePosition == 0 && args.View.ChildCount > 0 && args.View.GetChildAt(0).Top == 0) {
                            OpenChannel.GetChannel(_channelUrl, (channel, e) =>
                            {
                                if (e != null)
                                {
                                    //Error
                                    return;
                                }
                                var messagesQuery = channel.CreatePreviousMessageListQuery();
                                messagesQuery.Load(30, false, (messages, e2) =>
                                {
                                    if (e2 != null)
                                    {
                                        //Error
                                        return;
                                    }
                                    mAdapter.AddPreviousMessages(messages);

                                    mAdapter.NotifyDataSetChanged();
                                    mListView.SetSelection(messages.Count);
                                });

                            });


						}
					}
				};

				// Register Cache
				// Get max available VM memory, exceeding this amount will throw an OutOfMemory exception.
				// Stored in kilobytes as LruCache takes an int in its constructor.
				var cacheSize = (int)(Java.Lang.Runtime.GetRuntime().MaxMemory() / 16);

				mMemoryCache = new ImageUtils.MemoryLimitedLruCache(cacheSize);
			}

			private void TurnOffListViewDecoration(ListView listView)
			{
				listView.Divider = null;
				listView.DividerHeight = 0;
				listView.HorizontalFadingEdgeEnabled = false;
				listView.VerticalFadingEdgeEnabled = false;
				listView.HorizontalScrollBarEnabled = false;
				listView.VerticalScrollBarEnabled = false;
				listView.Selector = new ColorDrawable(Color.ParseColor("#ffffff"));
				listView.CacheColorHint = Color.ParseColor("#000000"); // For Gingerbread scrolling bug fix
			}

            private async Task<BaseMessage> SendUserMessageAsync(string messageText)
            {
                var channel = await GetChannelAsync();
                return await SendUserMessageTask(channel, messageText);
            }
            private Task<BaseMessage> SendUserMessageTask(OpenChannel channel, string messageText)
            {
                var t = new TaskCompletionSource<BaseMessage>();
                channel.SendUserMessage(messageText, (message, e) =>
                {
                    if (e != null)
                    {
                        t.TrySetException(e);
                        return;
                    }
                    t.TrySetResult(message);
                });
                return t.Task;
            }


            private async Task SendAsync()
            {
                var messageText = mEtxtMessage.Text.ToString();
                try
                {
                    var message = await SendUserMessageAsync(messageText);
                    mAdapter.AddMessage(message);
                    mAdapter.NotifyDataSetChanged();
                    mEtxtMessage.Text = string.Empty;
                }catch(Exception e)
                {
                    Console.WriteLine("Error Sending Text  -  " + e.Message);
                }
                
            }

            private async Task UploadAsync(Android.Net.Uri uri)
            {
                var channel = await GetChannelAsync();

                try
                {
                    // The projection contains the columns we want to return in our query.
                    string[] projection = new[] {
                        Android.Provider.MediaStore.Images.Media.InterfaceConsts.Data,
                        Android.Provider.MediaStore.Images.Media.InterfaceConsts.MimeType,
                        Android.Provider.MediaStore.Images.Media.InterfaceConsts.Size
                    };
                    using (ICursor cursor = this.Activity.ContentResolver.Query(uri, projection, null, null, null))
                    {
                        if (cursor != null)
                        {
                            cursor.MoveToFirst();
                            string path = cursor.GetString(cursor.GetColumnIndexOrThrow(Android.Provider.MediaStore.Images.Media.InterfaceConsts.Data));
                            string mime = cursor.GetString(cursor.GetColumnIndexOrThrow(Android.Provider.MediaStore.Images.Media.InterfaceConsts.MimeType));
                            int size = cursor.GetInt(cursor.GetColumnIndexOrThrow(Android.Provider.MediaStore.Images.Media.InterfaceConsts.Size));
                            cursor.Close();

                            if (path == null)
                            {
                                Toast.MakeText(this.Activity, "Uploading file must be located in local storage.", ToastLength.Long).Show();
                            }
                            else
                            {

                                var file = new SBFile(path);
                                channel.SendFileMessage(file, "filename.ext", mime, size, null, null, (FileMessage fileMessage, SendBirdException e) =>
                                {
                                    if (e != null)
                                    {
                                        // Error.
                                        return;
                                    }
                                });

                                //                        SendBirdClient.UploadFile(path, mime, size, "", new SendBirdFileUploadEventHandler(
                                //	(sender, e) => {
                                //		if(e.Exception != null) {
                                //			Console.WriteLine(e.Exception.StackTrace);
                                //			Toast.MakeText(this.Activity, "Fail to upload the file.", ToastLength.Long).Show();
                                //		}

                                //		SendBirdClient.SendFile(e.FileInfo);
                                //	}
                                //));
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.StackTrace);
                    Toast.MakeText(this.Activity, "Fail to upload the file.", ToastLength.Long).Show();
                }
            }

            public override void OnActivityResult(int requestCode, int resultCode, Intent data)
			{
				base.OnActivityResult(requestCode, resultCode, data);
				if (resultCode == (int)Android.App.Result.Ok) {
					if (requestCode == REQUEST_PICK_IMAGE && data != null && data.Data != null) {
						UploadAsync(data.Data);
					}
				}
			}

			public override void OnDestroy ()
			{
				base.OnDestroy ();
				GC.Collect();
			}
		}

		public class SendBirdChatAdapter : BaseAdapter<BaseMessage>
		{
			private const int TYPE_UNSUPPORTED = 0;
			private const int TYPE_MESSAGE = 1;
			private const int TYPE_SYSTEM_MESSAGE = 2;
			private const int TYPE_FILELINK = 3;
			private const int TYPE_BROADCAST_MESSAGE = 4;

			private Context mContext;
			private LayoutInflater mInflater;
			private List<BaseMessage> mItemList;

			internal long mMaxMessageTimestamp = long.MinValue;
			public long GetMaxMessageTimestamp()
			{
				return mMaxMessageTimestamp == long.MinValue ? long.MaxValue : mMaxMessageTimestamp;
			}

			internal long mMinMessageTimestamp = long.MaxValue;
			public long GetMinMessageTimestamp()
			{
				return mMinMessageTimestamp == long.MaxValue ? long.MinValue : mMinMessageTimestamp;
			}

			public SendBirdChatAdapter (Context context)
			{
				mContext = context;
				mInflater = mContext.GetSystemService (Context.LayoutInflaterService) as LayoutInflater;
				mItemList = new List<BaseMessage> ();
			}

			#region implemented abstract members of BaseAdapter

			public override long GetItemId (int position)
			{
				return position;
			}

			public override int Count {
				get {
					return mItemList.Count;
				}
			}

			public override BaseMessage this [int index] {
				get {
					return mItemList [index];
				}
			}

			public void Clear ()
			{
				mMaxMessageTimestamp = long.MinValue;
				mMinMessageTimestamp = long.MaxValue;
				mItemList.Clear ();
			}

            public void AddPreviousMessages(List<BaseMessage> prevMessages)
            {
                prevMessages.AddRange(mItemList);
                mItemList = prevMessages;
            }

			public void AddMessage (BaseMessage message)
			{
                if (message == null)
                {
                    return;
                }
				mItemList.Add (message);
				UpdateMessageTimestamp (message);

			}

			private void UpdateMessageTimestamp (BaseMessage message)
			{
				mMaxMessageTimestamp = mMaxMessageTimestamp < message.CreatedAt ? message.CreatedAt : mMaxMessageTimestamp;
				mMinMessageTimestamp = mMinMessageTimestamp > message.CreatedAt ? message.CreatedAt : mMinMessageTimestamp;
			}

			public override int GetItemViewType (int position)
			{
				BaseMessage item = mItemList [position];
				if (item is UserMessage) {
					return TYPE_MESSAGE;
				} else if (item is FileMessage) {
					return TYPE_FILELINK;
				} else if (item is AdminMessage) {
					return TYPE_SYSTEM_MESSAGE;
				} 

				return TYPE_UNSUPPORTED;
			}

			public override View GetView (int position, View convertView, ViewGroup parent)
			{
				ViewHolder viewHolder = null;
				BaseMessage item = this [position];

				if (convertView == null || (convertView.Tag as ViewHolder).GetViewType () != GetItemViewType (position)) {
					viewHolder = new ViewHolder ();
					viewHolder.SetViewType (GetItemViewType (position));

					switch (GetItemViewType (position)) {
					case TYPE_UNSUPPORTED:
						{
							convertView = new View (mInflater.Context);
							convertView.Tag = viewHolder;
							break;
						}
					case TYPE_MESSAGE:
						{
							convertView = mInflater.Inflate (Resource.Layout.SendBirdViewMessage, parent, false);
							viewHolder.SetView ("message", convertView.FindViewById (Resource.Id.txt_message) as TextView);
							viewHolder.SetView ("img_op_icon", (ImageView)convertView.FindViewById (Resource.Id.img_op_icon));
							convertView.Tag = viewHolder;
							break;
						}
					case TYPE_SYSTEM_MESSAGE:
						{
							convertView = mInflater.Inflate (Resource.Layout.SendBirdViewSystemMessage, parent, false);
							viewHolder.SetView ("message", convertView.FindViewById (Resource.Id.txt_message) as TextView);
							convertView.Tag = viewHolder;
							break;
						}
					case TYPE_BROADCAST_MESSAGE:
						{
							convertView = mInflater.Inflate (Resource.Layout.SendBirdViewSystemMessage, parent, false);
							viewHolder.SetView ("message", convertView.FindViewById (Resource.Id.txt_message) as TextView);
							convertView.Tag = viewHolder;
							break;
						}
					case TYPE_FILELINK:
						{
							TextView tv;
							convertView = mInflater.Inflate (Resource.Layout.SendBirdViewFileLink, parent, false);
							tv = convertView.FindViewById (Resource.Id.txt_sender_name) as TextView;

							viewHolder.SetView ("txt_sender_name", tv);
							viewHolder.SetView("img_op_icon", convertView.FindViewById(Resource.Id.img_op_icon) as ImageView);
							viewHolder.SetView("img_file_container", convertView.FindViewById(Resource.Id.img_file_container));
							viewHolder.SetView("image_container", convertView.FindViewById(Resource.Id.image_container));
							viewHolder.SetView("img_thumbnail", convertView.FindViewById(Resource.Id.img_thumbnail));
							viewHolder.SetView("txt_image_name", convertView.FindViewById(Resource.Id.txt_image_name));
							viewHolder.SetView("txt_image_size", convertView.FindViewById(Resource.Id.txt_image_size));

							viewHolder.SetView("file_container", convertView.FindViewById(Resource.Id.file_container));
							viewHolder.SetView("txt_file_name", convertView.FindViewById(Resource.Id.txt_file_name));
							viewHolder.SetView("txt_file_size", convertView.FindViewById(Resource.Id.txt_file_size));

							convertView.Tag = viewHolder;
							break;
						}
					}
				}
				viewHolder = convertView.Tag as ViewHolder;
				switch (GetItemViewType (position)) {
				case TYPE_UNSUPPORTED:
					break;
				case TYPE_MESSAGE:
					{
						var message = item as UserMessage;
						viewHolder.GetView<ImageView> ("img_op_icon").Visibility = ViewStates.Gone;
						var messageView = viewHolder.GetView<TextView> ("message");
						messageView.TextFormatted = Html.FromHtml ("<font color='#824096'><b>" + message.Sender.Nickname + "</b></font>: " + message.Message);
						if (!messageView.HasOnClickListeners) {
							// To prevent mutiple click listners
							messageView.Click += (sender, e) => {
								var builder = new Android.App.AlertDialog.Builder (mContext);
								builder.SetTitle ("SENDBIRD");
								builder.SetMessage("Do you want to start 1:1 messaging with " + message.Sender.Nickname + "?");
								builder.SetPositiveButton ("OK", (s, ev) => {
									Intent data = new Intent();
									data.PutExtra("userIds", new string[]{message.Sender.UserId});
									((SendBirdChatActivity)mContext).SetResult(Android.App.Result.Ok, data);
									((SendBirdChatActivity)mContext).mDoNotDisconnect = true;
									((SendBirdChatActivity)mContext).Finish();
								});
								builder.SetNegativeButton ("Cancel", (EventHandler<DialogClickEventArgs>)null);

								var dialog = builder.Create ();
								dialog.Show();
							};
						}
						break;
					}
				case TYPE_SYSTEM_MESSAGE:
					{
						AdminMessage systemMessage = item as AdminMessage;
						viewHolder.GetView<TextView> ("message").TextFormatted = Html.FromHtml (systemMessage.Message);
						break;
					}
				//case TYPE_BROADCAST_MESSAGE:
				//	{
				//		SendBird.Model.BroadcastMessage broadcastMessage = item as SendBird.Model.BroadcastMessage;
				//		viewHolder.GetView<TextView> ("message").TextFormatted = Html.FromHtml (broadcastMessage.message);
				//		break;
				//	}
				case TYPE_FILELINK:
					{
						FileMessage fileLink = item as FileMessage;
                           //fix this 
						//if(fileLink.IsOpenChannel() ){
						//	viewHolder.GetView<ImageView> ("img_op_icon").Visibility = ViewStates.Visible;
						//	viewHolder.GetView<TextView> ("txt_sender_name").TextFormatted = Html.FromHtml("&nbsp;&nbsp;&nbsp;<font color='#824096'><b>" + fileLink.GetSenderName() + "</b></font>: ");
						//} else {
						//	viewHolder.GetView<ImageView> ("img_op_icon").Visibility = ViewStates.Gone;
						//	viewHolder.GetView<TextView>("txt_sender_name").TextFormatted = Html.FromHtml("<font color='#824096'><b>" + fileLink.GetSenderName() + "</b></font>: ");
						//}
						if(fileLink.Type.ToLower().StartsWith("image")) {
							viewHolder.GetView("file_container").Visibility = ViewStates.Gone;

							viewHolder.GetView("image_container").Visibility = ViewStates.Visible;
							viewHolder.GetView<TextView>("txt_image_name").Text = fileLink.Name;
							viewHolder.GetView<TextView>("txt_image_size").Text = fileLink.Size.ToString();
							if (fileLink.Url != null && fileLink.Url != "null") {
								DisplayUrlImage (viewHolder.GetView<ImageView> ("img_thumbnail"), fileLink.Url);
							}
						} else {
							viewHolder.GetView("image_container").Visibility = ViewStates.Gone;

							viewHolder.GetView("file_container").Visibility = ViewStates.Visible;
							viewHolder.GetView<TextView>("txt_file_name").Text = fileLink.Name;
							viewHolder.GetView<TextView>("txt_file_size").Text = "" + fileLink.Size.ToString();
						}
						viewHolder.GetView("txt_sender_name").Click += (sender, e) => {
							var builder = new Android.App.AlertDialog.Builder (mContext);
							builder.SetTitle ("SENDBIRD");
							builder.SetMessage("Do you want to start 1:1 messaging with " + fileLink.Sender.Nickname + "?");
							builder.SetPositiveButton ("OK", (s, ev) => {
								Intent data = new Intent();
								data.PutExtra("userIds", new string[]{fileLink.Sender.UserId});
								((SendBirdChatActivity)mContext).SetResult(Android.App.Result.Ok, data);
								((SendBirdChatActivity)mContext).mDoNotDisconnect = true;
								((SendBirdChatActivity)mContext).Finish();
							});
							builder.SetNegativeButton ("Cancel", (EventHandler<DialogClickEventArgs>)null);

							var dialog = builder.Create ();
							dialog.Show();
						};
						break;
					}
				}

				return convertView;
			}

            internal void DeleteMessage(BaseChannel baseChannel, long messageId)
            {
                //TODO Implement
                //throw new NotImplementedException();
            }

            private class ViewHolder : Java.Lang.Object
			{
				private Dictionary<string, View> holder = new Dictionary<string, View> ();
				private int type;

				public int GetViewType ()
				{
					return this.type;
				}

				public void SetViewType (int type)
				{
					this.type = type;
				}

				public void SetView (string k, View v)
				{
					holder.Add (k, v);
				}

				public View GetView (string k)
				{
					return holder [k];
				}

				public T GetView<T> (string k)
				{
					return (T)Convert.ChangeType (GetView (k), typeof(T));
				}
			}

			#endregion
		}

		private static void DisplayUrlImage(ImageView imageView, string url)
		{
			int targetHeight = 256;
			int targetWidth = 256;

			if (mMemoryCache.Get (url) != null) {
				Bitmap cachedBM = (Bitmap)mMemoryCache.Get (url);
				imageView.SetImageBitmap (cachedBM);
			} else {
				WebClient webClient = new WebClient();
				webClient.DownloadDataCompleted += (sender, e) => {
					try {
						if(e.Error != null) {
							Console.WriteLine(e.Error.InnerException.StackTrace);
							Console.WriteLine(e.Error.InnerException.Message);
						} else {
							BitmapFactory.Options options = new BitmapFactory.Options ();
							options.InJustDecodeBounds = true; // <-- This makes sure bitmap is not loaded into memory.
							// Then get the properties of the bitmap
							BitmapFactory.DecodeByteArray(e.Result, 0, e.Result.Length, options);
							options.InSampleSize = ImageUtils.CalculateInSampleSize (options, targetWidth, targetHeight);
							options.InJustDecodeBounds = false;
							// Now we are loading it with the correct options. And saving precious memory.
							Bitmap bm = BitmapFactory.DecodeByteArray(e.Result, 0, e.Result.Length, options);
							mMemoryCache.Put(url, bm);
							imageView.SetImageBitmap(bm);
						}
					} catch(Exception ex) {
						Console.WriteLine(ex.StackTrace);
					}
					webClient.Dispose ();
				};
				webClient.DownloadDataAsync(new Uri(url));
			}
		}

		public static class Helper
		{
			public static void HideKeyboard (Android.App.Activity activity)
			{
				if (activity == null || activity.CurrentFocus == null) {
					return;
				}
				var imm = activity.GetSystemService (Context.InputMethodService) as Android.Views.InputMethods.InputMethodManager;
				imm.HideSoftInputFromWindow (activity.CurrentFocus.WindowToken, 0);
			}
		}
	}
}
