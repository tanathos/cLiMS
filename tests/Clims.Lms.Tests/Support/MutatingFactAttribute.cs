using System.Net;
using System.Text;
using System.Text.Json;

namespace Clims.Lms.Tests;

/// <summary>
/// Changes player state, so it needs a second opt-in: <c>CLIMS_LMS_ALLOW_WRITE=1</c> and <c>CLIMS_LMS_PLAYER=&lt;id&gt;</c>.
/// Use <see cref="PlayerSnapshot"/> to put the player back.
/// </summary>
public sealed class MutatingFactAttribute : FactAttribute
{
    public MutatingFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Integration.Url)
            || Environment.GetEnvironmentVariable("CLIMS_LMS_ALLOW_WRITE") != "1"
            || string.IsNullOrWhiteSpace(Integration.PlayerId))
        {
            Skip = "Mutating test: set CLIMS_LMS_URL, CLIMS_LMS_PLAYER and CLIMS_LMS_ALLOW_WRITE=1 (it changes the real player and restores it).";
        }
    }
}
