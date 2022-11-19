﻿namespace OctoshiftCLI.Commands;

public class CreateTeamCommandArgs : CommandArgs
{
    public string GithubOrg { get; set; }
    public string TeamName { get; set; }
    public string IdpGroup { get; set; }
    [Secret]
    public string GithubPat { get; set; }
}
