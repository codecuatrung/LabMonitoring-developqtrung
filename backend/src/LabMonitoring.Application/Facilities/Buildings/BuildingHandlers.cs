using FluentValidation;
using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Exceptions;
using LabMonitoring.Application.Common.Models;
using LabMonitoring.Domain.Facilities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LabMonitoring.Application.Facilities.Buildings;

// --- Queries ---

public sealed record GetBuildingsQuery(int Page = 1, int PageSize = 20) : IRequest<PagedResult<BuildingDto>>;

internal sealed class GetBuildingsHandler(IAppDbContext db) : IRequestHandler<GetBuildingsQuery, PagedResult<BuildingDto>>
{
    public async Task<PagedResult<BuildingDto>> Handle(GetBuildingsQuery request, CancellationToken ct)
    {
        var query = db.Buildings.OrderBy(b => b.Name);

        var total = await query.CountAsync(ct);
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new BuildingDto(b.Id, b.Name, b.Code, b.Rooms.Count))
            .ToListAsync(ct);

        return new PagedResult<BuildingDto>(items, total, page, pageSize);
    }
}

// --- Commands ---

public sealed record CreateBuildingCommand(string Name, string Code) : IRequest<int>;

public sealed class CreateBuildingValidator : AbstractValidator<CreateBuildingCommand>
{
    public CreateBuildingValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
    }
}

internal sealed class CreateBuildingHandler(IAppDbContext db) : IRequestHandler<CreateBuildingCommand, int>
{
    public async Task<int> Handle(CreateBuildingCommand request, CancellationToken ct)
    {
        if (await db.Buildings.AnyAsync(b => b.Code == request.Code, ct))
            throw new ConflictException($"Building code '{request.Code}' is already in use.");

        var building = new Building { Name = request.Name.Trim(), Code = request.Code.Trim() };
        db.Buildings.Add(building);
        await db.SaveChangesAsync(ct);
        return building.Id;
    }
}

public sealed record UpdateBuildingCommand(int Id, string Name, string Code) : IRequest;

public sealed class UpdateBuildingValidator : AbstractValidator<UpdateBuildingCommand>
{
    public UpdateBuildingValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
    }
}

internal sealed class UpdateBuildingHandler(IAppDbContext db) : IRequestHandler<UpdateBuildingCommand>
{
    public async Task Handle(UpdateBuildingCommand request, CancellationToken ct)
    {
        var building = await db.Buildings.FirstOrDefaultAsync(b => b.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Building), request.Id);

        if (await db.Buildings.AnyAsync(b => b.Code == request.Code && b.Id != request.Id, ct))
            throw new ConflictException($"Building code '{request.Code}' is already in use.");

        building.Name = request.Name.Trim();
        building.Code = request.Code.Trim();
        await db.SaveChangesAsync(ct);
    }
}

public sealed record DeleteBuildingCommand(int Id) : IRequest;

internal sealed class DeleteBuildingHandler(IAppDbContext db) : IRequestHandler<DeleteBuildingCommand>
{
    public async Task Handle(DeleteBuildingCommand request, CancellationToken ct)
    {
        var building = await db.Buildings.Include(b => b.Rooms).FirstOrDefaultAsync(b => b.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Building), request.Id);

        if (building.Rooms.Count > 0)
            throw new ConflictException("Remove the rooms in this building first.");

        db.Buildings.Remove(building);
        await db.SaveChangesAsync(ct);
    }
}
