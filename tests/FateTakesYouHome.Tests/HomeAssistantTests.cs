using System.Text.Json;
using FateTakesYouHome.HomeAssistant;
using FateTakesYouHome.HomeAssistant.Models;
using Xunit;

namespace FateTakesYouHome.Tests;

public sealed class HaConnectionOptionsTests
{
    private static HaConnectionOptions For(string url) =>
        new() { BaseUrl = url, AccessToken = new string('t', 64) };

    [Theory]
    [InlineData("http://homeassistant.local:8123", "ws://homeassistant.local:8123/api/websocket")]
    [InlineData("https://ha.example.com", "wss://ha.example.com/api/websocket")]
    [InlineData("http://192.0.2.10:8123/", "ws://192.0.2.10:8123/api/websocket")]
    public void BuildsTheWebSocketEndpoint(string input, string expected)
    {
        Assert.Equal(expected, For(input).WebSocketUri.ToString());
    }

    /// <summary>
    /// A bare host is treated as http, not as a relative URI.
    /// </summary>
    /// <remarks>
    /// People type "homeassistant.local:8123" constantly. Rejecting it, or worse resolving it as a
    /// scheme called "homeassistant", is a bad first impression for the one field that has to work.
    /// </remarks>
    [Fact]
    public void ABareHostIsAssumedToBeHttp()
    {
        Assert.Equal(
            "ws://homeassistant.local:8123/api/websocket",
            For("homeassistant.local:8123").WebSocketUri.ToString());
    }

    /// <summary>A reverse proxy often puts Home Assistant under a path prefix.</summary>
    [Fact]
    public void APathPrefixIsPreserved()
    {
        Assert.Equal(
            "wss://example.com/ha/api/websocket",
            For("https://example.com/ha").WebSocketUri.ToString());
    }

    [Theory]
    [InlineData("ws://host:8123", "http://host:8123/api/config")]
    [InlineData("wss://host", "https://host/api/config")]
    public void AWebSocketSchemeIsMappedBackForRest(string input, string expected)
    {
        Assert.Equal(expected, For(input).RestUri("api/config").ToString());
    }

    [Fact]
    public void TrailingSlashesDoNotDoubleUp()
    {
        Assert.Equal(
            "ws://host:8123/api/websocket",
            For("http://host:8123///").WebSocketUri.ToString());
    }

    [Fact]
    public void ValidationRejectsAnEmptyAddress()
    {
        var options = new HaConnectionOptions { BaseUrl = "", AccessToken = new string('t', 64) };

        Assert.Contains(options.Validate(), p => p.Contains("address", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidationRejectsATokenThatIsObviouslyNotOne()
    {
        var options = new HaConnectionOptions { BaseUrl = "http://host:8123", AccessToken = "abc" };

        Assert.Contains(options.Validate(), p => p.Contains("Long-Lived", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidationPassesForSomethingPlausible()
    {
        Assert.Empty(For("http://homeassistant.local:8123").Validate());
    }
}

public sealed class HaEntityStateTests
{
    private static HaEntityState Parse(string json) =>
        JsonSerializer.Deserialize<HaEntityState>(json)!;

    private const string KitchenLight =
        """
        {
          "entity_id": "light.kitchen_ceiling",
          "state": "on",
          "attributes": {
            "friendly_name": "Kitchen Ceiling",
            "brightness": 191,
            "supported_color_modes": ["color_temp", "hs"],
            "rgb_color": [255, 180, 90],
            "supported_features": 44,
            "is_dimmable": true
          }
        }
        """;

    [Fact]
    public void SplitsTheEntityIdIntoDomainAndObject()
    {
        HaEntityState state = Parse(KitchenLight);

        Assert.Equal("light", state.Domain);
        Assert.Equal("kitchen_ceiling", state.ObjectId);
    }

    [Fact]
    public void PrefersTheFriendlyNameWhenThereIsOne()
    {
        Assert.Equal("Kitchen Ceiling", Parse(KitchenLight).FriendlyName);
    }

    [Fact]
    public void FallsBackToAHumanisedObjectId()
    {
        HaEntityState state = Parse(
            """{ "entity_id": "switch.back_porch_heater", "state": "off", "attributes": {} }""");

        Assert.Equal("Back Porch Heater", state.FriendlyName);
    }

    [Theory]
    [InlineData("kitchen_ceiling", "Kitchen Ceiling")]
    [InlineData("heat_cool", "Heat Cool")]
    [InlineData("single", "Single")]
    [InlineData("", "")]
    public void HumanisesSnakeCase(string input, string expected)
    {
        Assert.Equal(expected, HaEntityState.HumaniseObjectId(input));
    }

    [Fact]
    public void ReadsTypedAttributes()
    {
        HaEntityState state = Parse(KitchenLight);

        Assert.Equal(191, state.AttrInt("brightness"));
        Assert.Equal("Kitchen Ceiling", state.AttrString("friendly_name"));
        Assert.True(state.AttrBool("is_dimmable"));
        Assert.Equal(["color_temp", "hs"], state.AttrStringList("supported_color_modes"));
        Assert.Equal((byte)255, state.AttrRgb("rgb_color")!.Value.R);
    }

    [Fact]
    public void MissingAttributesReturnNullRatherThanThrowing()
    {
        HaEntityState state = Parse(KitchenLight);

        Assert.Null(state.AttrString("nope"));
        Assert.Null(state.AttrInt("nope"));
        Assert.Null(state.AttrBool("nope"));
        Assert.Empty(state.AttrStringList("nope"));
        Assert.Null(state.AttrRgb("nope"));
    }

    /// <summary>
    /// Feature flags are a bitmask, and a partial match is not a match.
    /// </summary>
    [Fact]
    public void SupportsChecksTheWholeFlag()
    {
        // 44 == 32 | 8 | 4: transition, flash, effect.
        HaEntityState state = Parse(KitchenLight);

        Assert.True(state.Supports(HaFeatures.Light.Transition));
        Assert.True(state.Supports(HaFeatures.Light.Effect));
        Assert.False(state.Supports(64));
    }

    [Theory]
    [InlineData("on", true)]
    [InlineData("open", true)]
    [InlineData("playing", true)]
    [InlineData("heat", true)]
    [InlineData("off", false)]
    [InlineData("closed", false)]
    [InlineData("idle", false)]
    public void RecognisesTheManyWaysAnEntityIsOn(string value, bool expected)
    {
        HaEntityState state = Parse(
            $$"""{ "entity_id": "x.y", "state": "{{value}}", "attributes": {} }""");

        Assert.Equal(expected, state.IsOn);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("unknown")]
    [InlineData("")]
    public void TreatsTheAbsentStatesAsUnavailable(string value)
    {
        HaEntityState state = Parse(
            $$"""{ "entity_id": "x.y", "state": "{{value}}", "attributes": {} }""");

        Assert.True(state.IsUnavailable);
    }

    /// <summary>
    /// A lock that is "unlocked" reports IsOn, which is the opposite of what a padlock icon
    /// suggests but matches how every other domain reads: on means "not in the resting state".
    /// </summary>
    [Fact]
    public void AnUnlockedLockCountsAsOn()
    {
        HaEntityState state = Parse(
            """{ "entity_id": "lock.front", "state": "unlocked", "attributes": {} }""");

        Assert.True(state.IsOn);
    }
}

public sealed class HaDomainTests
{
    [Fact]
    public void EveryFirstClassDomainIsSupported()
    {
        foreach (string domain in HaDomains.FirstClass)
        {
            Assert.True(HaDomains.IsSupported(domain), $"'{domain}' is first class but not supported.");
        }
    }

    [Fact]
    public void NoDomainIsBothFirstClassAndReadOnly()
    {
        Assert.Empty(HaDomains.FirstClass.Intersect(HaDomains.ReadOnly));
    }

    [Fact]
    public void MomentaryDomainsAreAllFirstClass()
    {
        Assert.Empty(HaDomains.Momentary.Except(HaDomains.FirstClass));
    }

    [Theory]
    [InlineData("onoff", false)]
    [InlineData("brightness", true)]
    [InlineData("color_temp", true)]
    [InlineData("rgbww", true)]
    public void ColourModesReportWhetherTheyCarryBrightness(string mode, bool expected)
    {
        Assert.Equal(expected, HaColorModes.HasBrightness(mode));
    }

    [Theory]
    [InlineData("hs", true)]
    [InlineData("rgb", true)]
    [InlineData("color_temp", false)]
    [InlineData("brightness", false)]
    public void ColourModesReportWhetherTheyAllowAHue(string mode, bool expected)
    {
        Assert.Equal(expected, HaColorModes.IsFullColour(mode));
    }
}
