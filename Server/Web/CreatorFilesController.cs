using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeasonalPerks.Server.Seasons;
using SeasonalPerks.Shared.Seasons;

namespace SeasonalPerks.Server.Web;

[ApiController]
[Authorize(Policy = "Administrator")]
[Route("wtt-seasonal/creator")]
public sealed class CreatorFilesController(SeasonRepository repository) : ControllerBase
{
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
