// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace FateTakesYouHome.Services;

/// <summary>
/// Decides whether a notification is worth showing, given what was shown last.
/// </summary>
/// <remarks>
/// A key pressed again and again while the connection is down fails the same way every time. One
/// notification says so; a stack of identical ones is the app shouting. Anything different is
/// always shown, and so is the same thing again once <paramref name="window"/> has passed.
/// </remarks>
/// <param name="window">How long the same notice is shown only once.</param>
public sealed class RepeatedNoticeFilter(TimeSpan window)
{
    private (string Title, string Detail)? _last;
    private DateTime _lastShownAt;

    /// <summary>True when the notice should be shown now. Records it as shown if so.</summary>
    public bool ShouldShow(string title, string detail, DateTime now)
    {
        if (_last == (title, detail) && now - _lastShownAt < window)
        {
            return false;
        }

        _last = (title, detail);
        _lastShownAt = now;
        return true;
    }
}
