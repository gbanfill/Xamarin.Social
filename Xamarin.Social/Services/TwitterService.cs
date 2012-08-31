using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System.Text;
using System.Linq;

namespace Xamarin.Social.Services
{
	public class TwitterService : OAuth1Service
	{
		public TwitterService ()
			: base ("Twitter", "Twitter")
		{
			CreateAccountLink = new Uri ("https://twitter.com/signup");

			ShareTitle = "Tweet";

			MaxTextLength = 140;
			MaxLinks = int.MaxValue;
			MaxImages = 1;

			RequestTokenUrl = new Uri ("https://api.twitter.com/oauth/request_token");
			AuthorizeUrl = new Uri ("https://api.twitter.com/oauth/authorize");
			AccessTokenUrl = new Uri ("https://api.twitter.com/oauth/access_token");
		}

		public override int GetTextLength (Item item)
		{
			//
			// There are about 22 chars (eg https://t.co/UoGgfjFd) per attachment
			//
			return item.Text.Length + 22*(item.Links.Count + item.Images.Count + item.Files.Count);
		}

		public override Task ShareItemAsync (Item item, Account account, CancellationToken cancellationToken)
		{
			//
			// Combine the links into the tweet
			//
			var sb = new StringBuilder ();
			sb.Append (item.Text);
			foreach (var l in item.Links) {
				sb.Append (" ");
				sb.Append (l.AbsoluteUri);
			}
			var status = sb.ToString ();

			//
			// Create the request
			//
			Request req;
			if (item.Images.Count == 0) {
				req = CreateRequest ("POST", new Uri ("https://api.twitter.com/1/statuses/update.xml"), account);
				req.Parameters["status"] = status;
			}
			else {
				req = CreateRequest ("POST", new Uri ("https://upload.twitter.com/1/statuses/update_with_media.xml"), account);
				req.AddMultipartData ("status", status);
				foreach (var i in item.Images.Take (MaxImages)) {
					req.AddMultipartData ("media[]", i);
				}
			}

			//
			// Send it
			//
			return req.GetResponseAsync (cancellationToken).ContinueWith (reqTask => {
				var content = reqTask.Result.GetResponseText ();
				if (!content.Contains ("<status")) {
					throw new SocialException ("Twitter did not return the expected response.");
				}
			});
		}
	}
}
