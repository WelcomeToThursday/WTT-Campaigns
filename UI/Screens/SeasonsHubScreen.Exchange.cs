using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Controls;
using WTT.Campaigns.UI.Models;

namespace WTT.Campaigns.UI.Screens;

public sealed partial class SeasonsHubScreen
{
    private readonly Dictionary<string, int> _exchangeSources = new Dictionary<string, int>();
    private int _exchangeTarget = -1;
    private bool _exchangeCrate;
    private bool _exchangeModeChosen;

    private void OpenExchange()
    {
        if (HasTutorial)
        {
            return;
        }
        _exchangeSources.Clear();
        _exchangeTarget = -1;
        _exchangeCrate = false;
        _exchangeModeChosen = false;
        RenderExchange();
    }

    private void RenderExchange()
    {
        DismissDialog();
        HideTooltip();
        var overlay = Box(_stage, "HubTransactionDialog", 0, 0, 1920, 1080);
        _dialog = overlay.gameObject;
        UiElements.Fill(overlay, new Color(0, 0, 0, .72f), true);
        // Live level49 / ExchangeDocuments uses a 1150 x 565 window at 125% UI scale.
        var panel = Box(overlay, "ExchangePanel", 240, 165, 1440, 706);
        UiElements.Fill(panel, new Color(.015f, .015f, .015f, .97f), true);
        ExchangeFrame(panel, 0, 0, 1440, 706, new Color(.25f, .267f, .278f));
        UiElements.Fill(Box(panel, "TitleBar", 2, 2, 1436, 30), new Color(.13f, .13f, .13f));
        Caption(panel, "Title", "Exchange documents", 20, 12, 2, 1300, 30).color = new Color(.7f, .74f, .76f);
        var close = Button(panel, "CloseExchange", 1401, 6, 34, 22, DismissDialog, false);
        close.GetComponentInChildren<Text>().text = "";
        Art(close.transform, "CloseBackground", "sharedassets24-11", 0, 0, 34, 22);
        Art(close.transform, "CloseIcon", "resources-5551", 11, 4, 12, 14);
        var info = Art(panel, "ExchangeInfo", "sharedassets48-454", 1389, 55, 24, 24);
        Hint(
            info.gameObject,
            "Choose DOCUMENTS or CONTAINER, then select documents to spend.\n"
                + "Click an owned document to add one. Click a selected document above to remove one.\n"
                + "Documents cost "
                + _state.ExchangeRate
                + "; a container costs "
                + _state.CrateCost
                + ". Classified documents cannot be exchanged.",
            1190,
            250
        );

        var required = _exchangeCrate ? _state.CrateCost : _state.ExchangeRate;
        var selected = _exchangeSources.Values.Sum();
        Caption(panel, "SourceLabel", "Select documents to exchange:", 21, 104, 225, 430, 32).alignment = TextAnchor.MiddleCenter;
        for (var i = 0; i < _state.Documents.Length; i++)
        {
            var doc = _state.Documents[i];
            var count = _exchangeSources.TryGetValue(doc.Id, out var value) ? value : 0;
            var remaining = Math.Max(0, doc.Count - count);
            var x = 104 + i % 4 * 110;
            var y = 273 + i / 4 * 110;
            var tile = ExchangeTile(
                panel,
                "ExchangeSource" + i,
                remaining > 0 ? doc.Image : doc.UnavailableImage,
                x,
                y,
                remaining > 0 ? "x" + remaining : "",
                count > 0,
                () =>
                {
                    if (_exchangeModeChosen && count < doc.Count && selected < required && !_state.PreviewOnly)
                    {
                        _exchangeSources[doc.Id] = count + 1;
                        RenderExchange();
                    }
                },
                !_exchangeModeChosen || remaining == 0
            );
            tile.interactable = _exchangeModeChosen && remaining > 0 && selected < required && !_state.PreviewOnly;
            Hint(tile.gameObject, doc.Name + "\nOwned: " + doc.Count + "\nSelected: " + count, 240 + x, 165 + y - 95);
        }

        var sources = _state.Documents.Where(d => _exchangeSources.TryGetValue(d.Id, out var count) && count > 0).ToArray();
        for (var i = 0; i < sources.Length; i++)
        {
            var doc = sources[i];
            var count = _exchangeSources[doc.Id];
            var x = 720 - sources.Length * 55 + i * 110;
            var tile = ExchangeTile(
                panel,
                "SelectedSource" + i,
                doc.Image,
                x,
                90,
                "x" + count,
                false,
                () =>
                {
                    _exchangeSources[doc.Id] = count - 1;
                    RenderExchange();
                }
            );
            Hint(tile.gameObject, doc.Name + "\nClick to remove one.", 240 + x, 360);
        }

        var reason =
            _state.PreviewOnly ? "Not available in this preview."
            : _exchangeCrate ? _state.CrateUnavailableReason
            : _state.ExchangeUnavailableReason;
        var hasTarget = _exchangeModeChosen && (_exchangeCrate || _exchangeTarget >= 0 && _exchangeTarget < _state.Documents.Length);
        var canExchange = hasTarget && required > 0 && selected == required && reason.Length == 0;
        var arrowAsset = canExchange ? "sharedassets49-327" : "sharedassets49-295";
        Art(panel, "ExchangeArrowTop", arrowAsset, 605, 274, 230, 121.25f);
        Art(panel, "ExchangeArrowBottom", arrowAsset, 605, 382.3f, 230, 121.25f).rectTransform.localRotation = Quaternion.Euler(0, 0, 180);
        Art(panel, "ExchangeOutputBackground", "sharedassets48-476", 670, 339, 100, 100);
        ExchangeFrame(panel, 670, 339, 100, 100, new Color(.25f, .267f, .278f, .5f));
        if (hasTarget)
        {
            if (_exchangeCrate)
            {
                Art(panel, "ExchangeOutput", "sharedassets48-370", 672, 341, 96, 96).preserveAspect = true;
            }
            else
            {
                Remote(panel, "ExchangeOutput", _state.Documents[_exchangeTarget].Image, 672, 341, 96, 96);
            }
        }
        if (selected > 0 || _exchangeModeChosen)
        {
            Caption(panel, "ExchangeQuantity", selected + " / " + required, 24, 610, 518, 220, 36).alignment = TextAnchor.MiddleCenter;
        }

        if (!_exchangeModeChosen)
        {
            ExchangeModeButton(panel, "DOCUMENTS", "sharedassets49-370", 273, false);
            ExchangeModeButton(panel, "CONTAINER", "sharedassets49-315", 383, true);
        }
        else
        {
            var back = Button(
                panel,
                "ExchangeBack",
                860,
                224,
                36,
                36,
                () =>
                {
                    _exchangeModeChosen = false;
                    _exchangeTarget = -1;
                    _exchangeCrate = false;
                    if (selected > _state.ExchangeRate)
                        _exchangeSources.Clear();
                    RenderExchange();
                },
                false
            );
            back.GetComponentInChildren<Text>().text = "";
            Art(back.transform, "BackArrow", "resources-4672", 10, 5, 17, 26);
            Caption(panel, "TargetLabel", _exchangeCrate ? "Container" : "Select a document to receive:", 21, 904, 225, 430, 32).alignment =
                TextAnchor.MiddleCenter;
            if (_exchangeCrate)
            {
                Art(panel, "ContainerPreview", "sharedassets48-370", 904, 270, 430, 275).preserveAspect = true;
            }
            else
            {
                for (var i = 0; i < _state.Documents.Length; i++)
                {
                    var index = i;
                    var doc = _state.Documents[i];
                    var x = 904 + i % 4 * 110;
                    var y = 273 + i / 4 * 110;
                    var tile = ExchangeTile(
                        panel,
                        "ExchangeTarget" + i,
                        doc.Image,
                        x,
                        y,
                        "",
                        i == _exchangeTarget,
                        () =>
                        {
                            _exchangeTarget = index;
                            RenderExchange();
                        }
                    );
                    Hint(tile.gameObject, doc.Name, 240 + x, 165 + y - 85);
                }
            }
        }
        if (_exchangeModeChosen && reason.Length > 0)
        {
            Caption(panel, "ExchangeUnavailable", reason, 18, 870, 552, 510, 62).alignment = TextAnchor.MiddleCenter;
        }
        var confirm = Button(
            panel,
            "EXCHANGE",
            570,
            628,
            300,
            56,
            () =>
            {
                if (!canExchange)
                    return;
                var action = new HubAction
                {
                    Action = "exchange",
                    ExpectedRevision = _state.Revision,
                    Crate = _exchangeCrate,
                    DocumentId = _exchangeCrate ? "" : _state.Documents[_exchangeTarget].Id,
                    Sources = _exchangeSources.Where(p => p.Value > 0).ToDictionary(p => p.Key, p => p.Value),
                };
                DismissDialog();
                TransactionRequested?.Invoke(action);
            },
            false
        );
        confirm.interactable = canExchange;
        var label = confirm.GetComponentInChildren<Text>();
        label.fontSize = 38;
        label.color = canExchange ? UiElements.Ink : new Color(.28f, .28f, .26f);
        if (reason.Length > 0)
            Hint(confirm.gameObject, reason, 810, 710);
    }

    private void ExchangeModeButton(Transform panel, string label, string artwork, float y, bool crate)
    {
        var button = Button(
            panel,
            label,
            904,
            y,
            430,
            100,
            () =>
            {
                _exchangeModeChosen = true;
                _exchangeCrate = crate;
                _exchangeTarget = -1;
                RenderExchange();
            }
        );
        ((Image)button.targetGraphic).color = new Color(.073f, .086f, .094f);
        button.GetComponentInChildren<Text>().text = "";
        Art(button.transform, "ModeArtwork", artwork, 0, 0, 230, 100);
        Art(button.transform, "InnerShadow", "resources-4792", 1, 1, 428, 98).type = Image.Type.Sliced;
        Caption(button.transform, "ModeLabel", label, 30, 210, 0, 188, 100).alignment = TextAnchor.MiddleRight;
        ExchangeFrame(button.transform, 0, 0, 430, 100, new Color(.25f, .267f, .278f));
    }

    private Button ExchangeTile(
        Transform parent,
        string name,
        string image,
        float x,
        float y,
        string count,
        bool selected,
        Action action,
        bool muted = false
    )
    {
        var button = Button(parent, name, x, y, 100, 100, action);
        button.GetComponentInChildren<Text>().text = "";
        var background = (Image)button.targetGraphic;
        background.sprite = _art("sharedassets48-476");
        background.color = Color.white;
        Remote(button.transform, "Document", image, 1, 1, 98, 98);
        Art(button.transform, "InnerShadow", "sharedassets49-264", 1, 1, 98, 98).type = Image.Type.Sliced;
        if (muted)
        {
            UiElements.Fill(Box(button.transform, "DocumentFade", 1, 1, 98, 98), new Color(0, 0, 0, .65f));
        }
        ExchangeFrame(button.transform, 0, 0, 100, 100, selected ? new Color(.38f, .66f, .57f) : new Color(.25f, .267f, .278f));
        var amount = Caption(button.transform, "Count", count, 19, 4, 75, 92, 24);
        amount.color = UiElements.Ink;
        amount.alignment = TextAnchor.MiddleRight;
        return button;
    }

    private void ExchangeFrame(Transform parent, float x, float y, float width, float height, Color color)
    {
        var frame = Art(parent, "Frame", "resources-5972", x, y, width, height);
        frame.type = Image.Type.Sliced;
        frame.fillCenter = false;
        frame.color = color;
    }
}
