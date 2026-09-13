using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Server.Web;

[ApiController]
[Authorize(Policy = "Administrator")]
[Route("wtt-campaigns/creator")]
public sealed class CreatorFilesController(
    SeasonRepository repository,
    Authoring.ItemPreviewService previews,
    Authoring.StandaloneAssortRepository assorts,
    Authoring.TraderPortraitService portraits
) : ControllerBase
{
    [HttpGet("trader-portraits/{trader}")]
    public IActionResult TraderPortrait(string trader)
    {
        var portrait = portraits.Find(trader);
        return portrait == null ? NotFound() : PhysicalFile(portrait.Path, portrait.ContentType);
    }

    [HttpGet("assorts/{trader}/download")]
    public IActionResult Assortment(string trader, [FromQuery] long revision)
    {
        try
        {
            return File(assorts.Export(trader, revision), "application/json", "assort.json");
        }
        catch (Exception e) when (e is InvalidDataException or IOException or InvalidOperationException)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpGet("item-previews/{key}.png")]
    public IActionResult ItemPreview(string key)
    {
        var path = previews.ImagePath(key);
        return path == null ? NotFound() : PhysicalFile(path, "image/png");
    }

    [HttpGet("packs/{key}/download")]
    public IActionResult Download(string key)
    {
        try
        {
            return File(repository.Export(key), "application/zip", "season-" + key + ".zip");
        }
        catch (Exception e) when (e is InvalidDataException or IOException or InvalidOperationException)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpGet("assets/{id}.png")]
    public IActionResult Asset(string id)
    {
        var path = repository.AssetPath(id);
        return path == null ? NotFound() : PhysicalFile(path, "image/png");
    }
}
