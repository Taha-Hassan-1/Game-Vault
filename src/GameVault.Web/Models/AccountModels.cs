using System.ComponentModel.DataAnnotations;
using GameVault.Application.Catalogue;
using GameVault.Application.Reviews;
using GameVault.Domain;

namespace GameVault.Web.Models;

public class RegisterModel
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";

    [Required, StringLength(50, MinimumLength = 2), Display(Name = "Display name")]
    public string DisplayName { get; set; } = "";

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "The passwords do not match."), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";
}

public class LoginModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Display(Name = "Keep me signed in")]
    public bool RememberMe { get; set; }
}

public class ProfileModel
{
    [Required, StringLength(50, MinimumLength = 2), Display(Name = "Display name")]
    public string DisplayName { get; set; } = "";

    public string Email { get; set; } = "";
}

public class ReviewFormModel
{
    [Range(1, 5, ErrorMessage = "Choose a rating from 1 to 5 stars.")]
    public int Stars { get; set; }

    [StringLength(ReviewService.MaxBodyLength)]
    public string? Body { get; set; }

    public string Slug { get; set; } = "";
    public string GameTitle { get; set; } = "";
    public bool Exists { get; set; }
    public bool IsHidden { get; set; }
}

public sealed record GameDetailsViewModel(
    GameDetails Game, string? CurrentUserId, IReadOnlySet<ListKind> Lists, MyReview? MyReview)
{
    public bool IsSignedIn => CurrentUserId is not null;
}

public sealed record ListsViewModel(IReadOnlyList<GameCard> Wishlist, IReadOnlyList<GameCard> Played);
