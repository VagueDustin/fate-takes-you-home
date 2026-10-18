// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using FateTakesYouHome.Models;
using FateTakesYouHome.ViewModels;
using Xunit;

namespace FateTakesYouHome.Tests;

/// <summary>Giving a layout widget a shorter name of its own.</summary>
public sealed class WidgetRenameTests
{
    private static EditorWidget Widget(string? label = null) =>
        new(new WidgetSpec { Kind = WidgetKind.QuickAction, EntityId = "scene.x", Label = label },
            "A Very Long Scene Name", "Quick action");

    [Fact]
    public void ANewNameIsSavedWithTheLayout()
    {
        EditorWidget widget = Widget();
        bool renamed = false;
        widget.Renamed += (_, _) => renamed = true;

        widget.Title = "  Bright  ";

        Assert.True(renamed);
        Assert.Equal("Bright", widget.Title);
        Assert.Equal("Bright", widget.Commit().Label);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A Very Long Scene Name")]
    public void ClearingTheNameOrTypingTheDefaultGoesBackToTheEntitysOwn(string typed)
    {
        EditorWidget widget = Widget("Bright");

        widget.Title = typed;

        Assert.Equal("A Very Long Scene Name", widget.Title);
        Assert.Null(widget.Commit().Label);
    }

    [Fact]
    public void TheNameSurvivesCopyingTheLayout() =>
        Assert.Equal("Bright", new WidgetSpec { Label = "Bright" }.Clone().Label);
}
