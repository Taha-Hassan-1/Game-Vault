using GameVault.Application.Catalogue;

namespace GameVault.Web.Models;

public sealed record GameSectionModel(string Title, IReadOnlyList<GameCard> Games, string? Link = null);
