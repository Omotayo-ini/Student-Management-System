using Microsoft.AspNetCore.Mvc;
using StudentManagement.Models;
using StudentManagement.Services;

namespace StudentManagement.Controllers;

[ApiController]
[Route("api/voice-commands")]
public sealed class VoiceCommandsController : ControllerBase
{
    private readonly VoiceCommandService _voiceCommandService;

    public VoiceCommandsController(VoiceCommandService voiceCommandService)
    {
        _voiceCommandService = voiceCommandService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(VoiceCommandResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<VoiceCommandResponse>> Execute(
        [FromBody] VoiceCommandRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var response = await _voiceCommandService.ExecuteAsync(request, cancellationToken);
        return Ok(response);
    }
}
