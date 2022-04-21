//-----------------------------------------------------------------------
// <copyright file="Toaster.cs" company="Studio A&T s.r.l.">
//     Author: nicogis
//     Copyright (c) Studio A&T s.r.l. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
namespace StudioAT.Mobile.iOS.Classes
{
    using Foundation;
    using UIKit;

    public static class Toaster
    {
        const double LONG_DELAY = 3.5;
        const double SHORT_DELAY = 2.0;

        public static void LongAlert(string message)
        {
            ShowAlert(message, LONG_DELAY);
        }
        public static void ShortAlert(string message)
        {
            ShowAlert(message, SHORT_DELAY);
        }

        private static void ShowAlert(string message, double seconds)
        {
            var alert = UIAlertController.Create(null, message, UIAlertControllerStyle.Alert);
            NSTimer.CreateScheduledTimer(seconds, (obj) =>
            {
                DismissMessage(alert, obj);
            });

            UIApplication.SharedApplication.KeyWindow.RootViewController.PresentViewController(alert, true, null);
        }

        private static void DismissMessage(UIAlertController alert, NSTimer alertDelay)
        {
            alert?.DismissViewController(true, null);
            alertDelay?.Dispose();
        }
    }
}