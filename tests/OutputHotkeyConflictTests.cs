using System.Reflection;
using KeySender.Services;
using KeySender.Validation;
using MioKit.KeySender.Tests.Fakes;
using MioKit.Sdk;
using Xunit;

namespace MioKit.KeySender.Tests;

public sealed class OutputHotkeyConflictTests
{
    [Theory]
    [InlineData("RightControl", KeyCode.Control)]
    [InlineData("RightAlt", KeyCode.Menu)]
    [InlineData("RightShift", KeyCode.Shift)]
    [InlineData("RightWin", KeyCode.LWin)]
    public void Output_chord_matches_host_normalized_modifier(string token, KeyCode normalized)
    {
        var hotkeys = WithRegistration(HotKeyStatus.Registered, normalized, KeyCode.F24);

        Assert.Equal(ValidationCodes.OutputHotkeyConflict,
            OutputHotkeyConflict.FindConflict(hotkeys, ["F24", token]));
    }

    [Fact]
    public void Pressing_both_modifier_sides_counts_as_one_host_key()
    {
        var hotkeys = WithRegistration(HotKeyStatus.Registered, KeyCode.Control, KeyCode.C);

        Assert.Equal(ValidationCodes.OutputHotkeyConflict,
            OutputHotkeyConflict.FindConflict(hotkeys, ["LeftControl", "RightControl", "KeyC"]));
    }

    [Fact]
    public void Exact_match_excludes_a_mapped_extra_key_and_a_modifier_only_prefix()
    {
        var hotkeys = WithRegistration(HotKeyStatus.Registered, KeyCode.Control, KeyCode.C);

        Assert.Null(OutputHotkeyConflict.FindConflict(hotkeys, ["LeftControl"]));
        Assert.Null(OutputHotkeyConflict.FindConflict(hotkeys, ["LeftControl", "KeyC", "Digit4"]));
    }

    [Theory]
    [InlineData(HotKeyStatus.Disabled)]
    [InlineData(HotKeyStatus.Unregistered)]
    public void Inactive_registration_does_not_block_matching_output(HotKeyStatus status)
    {
        var hotkeys = WithRegistration(status, KeyCode.Control, KeyCode.C);

        Assert.Null(OutputHotkeyConflict.FindConflict(hotkeys, ["LeftControl", "KeyC"]));
    }

    [Fact]
    public void Missing_hotkey_service_allows_the_output_check_to_be_skipped()
    {
        Assert.Null(OutputHotkeyConflict.FindConflict(null, ["LeftControl", "KeyC"]));
    }

    private static IGlobalHotKeyService WithRegistration(HotKeyStatus status, params KeyCode[] keys)
    {
        var service = DispatchProxy.Create<IGlobalHotKeyService, HotKeyRegistrationProxy>();
        ((HotKeyRegistrationProxy)(object)service).Registrations =
        [
            new HotKeyRegistration(
                "example", null, new HotKeyInfo(HotKeyInfo.TypeExactMatch, keys),
                SuppressEvent: true, StopPropagation: true, Status: status),
        ];
        return service;
    }
}
