using HizenLabs.Shared;
using Oxide.Game.Rust.Cui;

using UnityEngine;


#if CARBON
using Carbon.Components;
using static Carbon.Components.CUI;
#else
using Oxide.Game.Rust.Cui;
using Oxide.Plugins;
#endif

namespace HizenLabs.Plugins.TestPlugin;

[Description("Plugin for testing purposes.")]
public class TestPlugin : PluginBase
{
    [ChatCommand("test")]
    private void TestCommand(BasePlayer player, string command, string[] args)
    {
        Puts("testing!");
#if CARBON
        using CUI cui = new CUI(CuiHandler);

        cui.v2.CreateParent(CUI.ClientPanels.Overlay, LuiPosition.Full, "Container");

        cui.v2.CreatePanel("Container",
            new LuiPosition(0f, 0f, 1f, 1f),
            new LuiOffset(208f, 160f, -304f, -128f),
            "0.09 0.09 0.11 1", "Panel.1");

        cui.v2.CreatePanel("Panel.1",
            new LuiPosition(0f, 1f, 1f, 1f),
            new LuiOffset(0f, -88f, 0f, 0f),
            "0.337 0.314 0.624 1", "Panel.2");

        cui.v2.CreateText("Panel.2",
            new LuiPosition(0.5f, 0.5f, 0.5f, 0.5f),
            new LuiOffset(-384f, -36f, 384f, 44f),
            20, "0 0 0 1", "Testing!\n123", TextAnchor.MiddleCenter, "Text.7");

        cui.v2.CreatePanel("Panel.1",
            new LuiPosition(1f, 0f, 1f, 0f),
            new LuiOffset(-132f, 12f, -12f, 52f),
            "0.922 0.655 0.925 1", "Panel.3");

        cui.v2.SendUi(player);
#else
var container = new CuiElementContainer();

container.Add(new CuiPanel
{
    Image = { Color = "0.09 0.09 0.11 1" },
    RectTransform =
    {
        AnchorMin = "0 0",
        AnchorMax = "1 1",
        OffsetMin = "208 160",
        OffsetMax = "-304 -128"
    }
}, "Overlay", "Panel.1");

container.Add(new CuiPanel
{
    Image = { Color = "0.337 0.314 0.624 1" },
    RectTransform =
    {
        AnchorMin = "0 1",
        AnchorMax = "1 1",
        OffsetMin = "0 -88",
        OffsetMax = "0 0"
    }
}, "Panel.1", "Panel.2");

container.Add(new CuiLabel
{
    Text =
    {
        Text = "Testing!\n123",
        FontSize = 20,
        Font = "robotocondensed-regular.ttf",
        Align = TextAnchor.MiddleCenter,
        Color = "0 0 0 1"
    },
    RectTransform =
    {
        AnchorMin = "0.5 0.5",
        AnchorMax = "0.5 0.5",
        OffsetMin = "-384 -36",
        OffsetMax = "384 44"
    }
}, "Panel.2", "Text.7");

container.Add(new CuiPanel
{
    Image = { Color = "0.922 0.655 0.925 1" },
    RectTransform =
    {
        AnchorMin = "1 0",
        AnchorMax = "1 0",
        OffsetMin = "-132 12",
        OffsetMax = "-12 52"
    }
}, "Panel.1", "Panel.3");

CuiHelper.AddUi(player, container);
#endif
    }

    [ChatCommand("clear")]
    private void ClearCommand(BasePlayer player, string command, string[] args)
    {
#if CARBON
        CuiHandler.Destroy("Panel.1", player);
#else
        CuiHelper.DestroyUi(player, "Panel.1");
#endif
    }
}
