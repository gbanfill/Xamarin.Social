//
//  Copyright 2012-2013, Xamarin Inc.
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//        http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.

using System;
using System.Collections.Generic;
using CoreGraphics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreGraphics;
using Foundation;
using UIKit;
using Xamarin.Auth;
using Xamarin.Controls;
using Xamarin.Utilities.iOS;

namespace Xamarin.Social
{
    public class ShareViewController : UIViewController
    {
        Service service;
        Item item;
        List<Account> accounts = new List<Account>();
        Action<ShareResult> completionHandler;
        Task<IEnumerable<Account>> futureAccounts;

        UITextView textEditor;
        ProgressLabel progress;
        TextLengthLabel textLengthLabel;
        UILabel linksLabel;
        ChoiceField accountField = null;

        bool sharing = false;
        bool canceledFromOutside = false;

        UIAlertView accountsAlert;

        static UIFont TextEditorFont = UIFont.SystemFontOfSize(18);
        static readonly UIColor FieldColor = UIColor.FromRGB(56, 84, 135);

        internal ShareViewController(Service service, Item item, Action<ShareResult> completionHandler)
        {
            this.service = service;
            this.item = item;
            this.completionHandler = completionHandler;

            Title = NSBundle.MainBundle.LocalizedString(service.ShareTitle, "Title of Share dialog");
			
            View.BackgroundColor = UIColor.White;

            if (UIDevice.CurrentDevice.CheckSystemVersion(7, 0))
                EdgesForExtendedLayout = UIRectEdge.None;

            futureAccounts = service.GetAccountsAsync();
        }

        public override void ViewDidLoad()
        {
            BuildUI();
            base.ViewDidLoad();
        }

        public override void ViewDidAppear(bool animated)
        {
            base.ViewDidAppear(animated);

            var fa = Interlocked.Exchange(ref futureAccounts, null);
            if (fa != null)
            {
                fa.ContinueWith(t =>
                    {
                        accounts.AddRange(t.Result);
                        foreach (string username in accounts.Select (a => a.Username))
                            accountField.Items.Add(username);

                        CheckForAccounts();
                    }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            else if (canceledFromOutside)
                canceledFromOutside = false;
            else
                CheckForAccounts();
        }

        void CheckForAccounts()
        {
            if (accounts.Count == 0)
            {

                var title = "No " + service.Title + " Accounts";
                var msg = "There are no configured " + service.Title + " accounts. " +
                          "Would you like to add one?";

                accountsAlert = new UIAlertView(
                    title,
                    msg,
                    null,
                    "Cancel",
                    "Add Account");

                accountsAlert.Clicked += (sender, e) =>
                {
                    if (e.ButtonIndex == 1)
                    {
                        Authenticate();
                    }
                    else
                    {
                        completionHandler(ShareResult.Cancelled);
                    }
                };

                accountsAlert.Show();
            }
            else
            {
                textEditor.BecomeFirstResponder();
            }
        }

        void Authenticate()
        {
            var vc = service.GetAuthenticateUI(account =>
                {
                    if (account != null)
                        accounts.Add(account);
                    else
                        canceledFromOutside = true;

                    DismissViewController(true, () =>
                        {
                            if (account != null)
                            {
                                accountField.Items.Add(account.Username);
                                textEditor.BecomeFirstResponder();
                            }
                            else
                            {
                                completionHandler(ShareResult.Cancelled);
                            }
                        });
                });
            vc.ModalTransitionStyle = UIModalTransitionStyle.FlipHorizontal;
            PresentViewController(vc, true, null);
        }

        void BuildUI()
        {
            var b = View.Bounds;

            var statusHeight = 22.0f;

            //
            // Account Field
            //
            var fieldHeight = 33;

            accountField = new ChoiceField(
                new CGRect(0, b.Y, b.Width, 33),
                this,
                NSBundle.MainBundle.LocalizedString("From", "From title when sharing"));
            View.AddSubview(accountField);
            b.Y += fieldHeight;
            b.Height -= fieldHeight;

            //
            // Text Editor
            //
            var editorHeight = b.Height;
            if (service.HasMaxTextLength || item.Links.Count > 0)
            {
                editorHeight -= statusHeight;
            }
            textEditor = new UITextView(new CGRect(0, b.Y, b.Width, editorHeight))
            {
                Font = TextEditorFont,
                AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight,
                Text = item.Text,
            };
            textEditor.Delegate = new TextEditorDelegate(this);
            View.AddSubview(textEditor);

            //
            // Icons
            //
            if (item.Images.Count > 0)
            {

                var rem = 4.0f;
                CGRect f;
                var x = b.Right - AttachmentIcon.Size - 8 - rem * (item.Images.Count - 1);
                var y = textEditor.Frame.Y + 8;

                f = textEditor.Frame;
                f.Width = x - 8 - f.X;
                textEditor.Frame = f;

                foreach (var i in item.Images)
                {
                    var icon = new ImageIcon(i.Image);

                    f = icon.Frame;
                    f.X = x;
                    f.Y = y;
                    icon.Frame = f;

                    View.AddSubview(icon);

                    x += rem;
                    y += rem;
                }
            }

            //
            // Remaining Text Length
            //
            if (service.HasMaxTextLength)
            {
                textLengthLabel = new TextLengthLabel(
                    new CGRect(4, b.Bottom - statusHeight, textEditor.Frame.Width - 8, statusHeight),
                    service.MaxTextLength)
                {
                    TextLength = service.GetTextLength(item),
                };
                View.AddSubview(textLengthLabel);
            }
			
            //
            // Links Label
            //
            if (item.Links.Count > 0)
            {
                linksLabel = new UILabel(
                    new CGRect(4, b.Bottom - statusHeight, textEditor.Frame.Width - 66, statusHeight))
                {
                    TextColor = UIColor.FromRGB(124, 124, 124),
                    AutoresizingMask =
						UIViewAutoresizing.FlexibleTopMargin |
                    UIViewAutoresizing.FlexibleBottomMargin |
                    UIViewAutoresizing.FlexibleWidth,

                    UserInteractionEnabled = false,
                    BackgroundColor = UIColor.Clear,
                    Font = UIFont.SystemFontOfSize(16),
                    LineBreakMode = UILineBreakMode.HeadTruncation,
                };
                if (item.Links.Count == 1)
                {
                    linksLabel.Text = item.Links[0].AbsoluteUri;
                }
                else
                {
                    linksLabel.Text = string.Format(
                        NSBundle.MainBundle.LocalizedString("{0} links", "# of links label"),
                        item.Links.Count);
                }
                View.AddSubview(linksLabel);
            }

            //
            // Navigation Items
            //
            NavigationItem.LeftBarButtonItem = new UIBarButtonItem(
                UIBarButtonSystemItem.Cancel,
                delegate
                {
                    completionHandler(ShareResult.Cancelled);
                });


            NavigationItem.RightBarButtonItem = new UIBarButtonItem(
                NSBundle.MainBundle.LocalizedString("Send", "Send button text when sharing"),
                UIBarButtonItemStyle.Done,
                HandleSend);

            //
            // Watch for the keyboard
            //
            NSNotificationCenter.DefaultCenter.AddObserver(UIKeyboard.DidShowNotification, HandleKeyboardDidShow);
            NSNotificationCenter.DefaultCenter.AddObserver(UIKeyboard.WillHideNotification, HandleKeyboardDidHide);
        }

        void HandleSend(object sender, EventArgs e)
        {
            if (sharing)
                return;
				
            item.Text = textEditor.Text;
			
            StartSharing();

            var account = accounts.FirstOrDefault();
            if (accounts.Count > 1 && accountField != null)
            {
                account = accounts.FirstOrDefault(x => x.Username == accountField.SelectedItem);
            }
			
            try
            {
                service.ShareItemAsync(item, account).ContinueWith(shareTask =>
                    {
					
                        StopSharing();
					
                        if (shareTask.IsFaulted)
                        {
                            this.ShowError("Share Error", shareTask.Exception);
                        }
                        else
                        {
                            completionHandler(ShareResult.Done);
                        }
					
                    }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            catch (Exception ex)
            {
                StopSharing();
                this.ShowError("Share Error", ex);
            }
        }

        void StartSharing()
        {
            sharing = true;
            NavigationItem.RightBarButtonItem.Enabled = false;

            if (progress == null)
            {
                progress = new ProgressLabel(NSBundle.MainBundle.LocalizedString("Sending...", "Sending... status message when sharing"));
                NavigationItem.TitleView = progress;
                progress.StartAnimating();
            }
        }

        void StopSharing()
        {
            sharing = false;
            NavigationItem.RightBarButtonItem.Enabled = true;

            if (progress != null)
            {
                progress.StopAnimating();
                NavigationItem.TitleView = null;
                progress = null;
            }
        }

        public override bool ShouldAutorotateToInterfaceOrientation(UIInterfaceOrientation toInterfaceOrientation)
        {
            return true;
        }

        void ResignFirstResponders()
        {
            textEditor.ResignFirstResponder();
        }

        void HandleKeyboardDidShow(NSNotification n)
        {
            var size = UIKeyboard.BoundsFromNotification(n).Size;
			
            var f = textEditor.Frame;
            f.Height -= size.Height;
            textEditor.Frame = f;

            if (textLengthLabel != null)
            {
                f = textLengthLabel.Frame;
                f.Y -= size.Height;
                textLengthLabel.Frame = f;
            }

            if (linksLabel != null)
            {
                f = linksLabel.Frame;
                f.Y -= size.Height;
                linksLabel.Frame = f;
            }
        }

        void HandleKeyboardDidHide(NSNotification n)
        {
            var size = UIKeyboard.BoundsFromNotification(n).Size;
			
            UIView.BeginAnimations("kbd");
			
            var f = textEditor.Frame;
            f.Height += size.Height;
            textEditor.Frame = f;

            if (textLengthLabel != null)
            {
                f = textLengthLabel.Frame;
                f.Y += size.Height;
                textLengthLabel.Frame = f;
            }

            if (linksLabel != null)
            {
                f = linksLabel.Frame;
                f.Y += size.Height;
                linksLabel.Frame = f;
            }
			
            UIView.CommitAnimations();
        }

        class TextEditorDelegate : UITextViewDelegate
        {
            ShareViewController controller;

            public TextEditorDelegate(ShareViewController controller)
            {
                this.controller = controller;
            }

            public override void Changed(UITextView textView)
            {
                controller.item.Text = textView.Text;
                if (controller.textLengthLabel != null)
                {
                    controller.textLengthLabel.TextLength =
						controller.service.GetTextLength(controller.item);
                }
            }
        }

        class TextLengthLabel : UILabel
        {
            int maxLength;
            int textLength;

            static readonly UIColor okColor = UIColor.FromRGB(124, 124, 124);
            static readonly UIColor errorColor = UIColor.FromRGB(166, 80, 80);

            public int TextLength
            {
                get
                {
                    return textLength;
                }
                set
                {
                    textLength = value;
                    Update();
                }
            }

            public TextLengthLabel(CGRect frame, int maxLength)
                : base(frame)
            {
                this.maxLength = maxLength;
                this.textLength = 0;
                UserInteractionEnabled = false;
                BackgroundColor = UIColor.Clear;
                AutoresizingMask = 
					UIViewAutoresizing.FlexibleWidth |
                UIViewAutoresizing.FlexibleBottomMargin |
                UIViewAutoresizing.FlexibleTopMargin;
                TextAlignment = UITextAlignment.Right;
                Font = UIFont.BoldSystemFontOfSize(16);
                TextColor = okColor;
            }

            void Update()
            {
                var rem = maxLength - textLength;
                Text = rem.ToString();
                if (rem < 0)
                {
                    TextColor = errorColor;
                }
                else
                {
                    TextColor = okColor;
                }
            }
        }

        abstract class AttachmentIcon : UIImageView
        {
            public static float Size { get { return 72; } }

            static readonly CGColor borderColor = new CGColor(0.75f, 0.75f, 0.75f);
            static readonly CGColor shadowColor = new CGColor(0.25f, 0.25f, 0.25f);

            public AttachmentIcon()
                : base(new CGRect(0, 0, Size, Size))
            {
                ContentMode = UIViewContentMode.ScaleAspectFill;
                ClipsToBounds = true;
                AutoresizingMask = UIViewAutoresizing.FlexibleLeftMargin;

                Layer.CornerRadius = 4;
                Layer.ShadowOffset = new CGSize(0, 0);
                Layer.ShadowColor = shadowColor;
                Layer.ShadowRadius = 4;
                Layer.ShadowOpacity = 1.0f;
                Layer.BorderColor = borderColor;
                Layer.BorderWidth = 1;
            }
        }

        class ImageIcon : AttachmentIcon
        {
            public ImageIcon(UIImage image)
            {
                Image = image;
            }
        }

        abstract class Field : UIView
        {
            public ShareViewController Controller { get; private set; }

            public UILabel TitleLabel { get; private set; }

            public Field(CGRect frame, ShareViewController controller, string title)
                : base(frame)
            {
                Controller = controller;

                BackgroundColor = UIColor.White;
                Opaque = true;
                AutoresizingMask = UIViewAutoresizing.FlexibleWidth;

                TitleLabel = new UILabel()
                {
                    BackgroundColor = UIColor.White,
                    Font = TextEditorFont,
                    Text = title + ":",
                    TextColor = UIColor.Gray,
                };

                var w = UIStringDrawing.StringSize(TitleLabel.Text, TextEditorFont).Width + 8;
                TitleLabel.Frame = new CGRect(8, 0, w, frame.Height - 1);

                AddSubview(TitleLabel);
            }

            public override void Draw(CGRect rect)
            {
                var b = Bounds;
                using (var c = UIGraphics.GetCurrentContext())
                {
                    UIColor.LightGray.SetStroke();
                    c.SetLineWidth(1.0f);
                    c.MoveTo(0, b.Bottom);
                    c.AddLineToPoint(b.Right, b.Bottom);
                    c.StrokePath();
                }
            }
        }

        class ChoiceField : Field
        {
            public string SelectedItem
            {
                get { return Picker.SelectedItem; }
            }

            public LabelButton ValueLabel { get; private set; }

            public CheckedPickerView Picker { get; private set; }

            public IList<string> Items
            {
                get { return Picker.Items; }
                set { Picker.Items = value; }
            }

            public ChoiceField(CGRect frame, ShareViewController controller, string title)
                : base(frame, controller, title)
            {
                ValueLabel = new LabelButton()
                {
                    BackgroundColor = UIColor.White,
                    Font = TextEditorFont,
                    TextColor = UIColor.DarkTextColor,
                    AutoresizingMask = UIViewAutoresizing.FlexibleWidth,
                };				
                var tf = TitleLabel.Frame;
                ValueLabel.Frame = new CGRect(tf.Right, 0, frame.Width - tf.Right, frame.Height - 1);

                ValueLabel.TouchUpInside += HandleTouchUpInside;

                AddSubview(ValueLabel);

                Picker = new CheckedPickerView(new CGRect(0, 0, 320, 216));
                Picker.Hidden = true;
                Picker.SelectedItemChanged += delegate
                {
                    ValueLabel.Text = Picker.SelectedItem;
                };
                controller.View.AddSubview(Picker);

                ValueLabel.Text = Picker.SelectedItem;
            }

            void HandleTouchUpInside(object sender, EventArgs e)
            {
                if (Items.Count > 1)
                {
                    Controller.ResignFirstResponders();

                    var v = Controller.View;

                    Picker.Hidden = false;
                    Picker.Frame = new CGRect(0, v.Bounds.Bottom - 216, 320, 216);
                    v.BringSubviewToFront(Picker);
                }
            }
        }

        class LabelButton : UILabel
        {
            public event EventHandler TouchUpInside;

            public LabelButton()
            {
                UserInteractionEnabled = true;
            }

            public override void TouchesBegan(NSSet touches, UIEvent evt)
            {
                TextColor = FieldColor;
            }

            public override void TouchesEnded(NSSet touches, UIEvent evt)
            {
                TextColor = UIColor.DarkTextColor;

                var t = touches.ToArray<UITouch>().First();
                if (Bounds.Contains(t.LocationInView(this)))
                {
                    var ev = TouchUpInside;
                    if (ev != null)
                    {
                        ev(this, EventArgs.Empty);
                    }
                }
            }

            public override void TouchesCancelled(NSSet touches, UIEvent evt)
            {
                TextColor = UIColor.DarkTextColor;
            }
        }
    }
}



