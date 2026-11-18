using MongoDB.Driver;
using System.Linq.Expressions;
namespace ProyectAPI.Repositories;

internal static class TournamentEconomyMongo
{
    public static IFindFluent<T,T> ScopedFind<T>(this IMongoCollection<T> c, Expression<Func<T,bool>> f) =>
        TournamentEconomyRepository.Session is {} s ? c.Find(s,f) : c.Find(f);
    public static Task ScopedInsertOneAsync<T>(this IMongoCollection<T> c,T v,InsertOneOptions? o,CancellationToken ct) =>
        TournamentEconomyRepository.Session is {} s ? c.InsertOneAsync(s,v,o,ct) : c.InsertOneAsync(v,o,ct);
    public static Task ScopedInsertManyAsync<T>(this IMongoCollection<T> c,IEnumerable<T> v,InsertManyOptions? o,CancellationToken ct) =>
        TournamentEconomyRepository.Session is {} s ? c.InsertManyAsync(s,v,o,ct) : c.InsertManyAsync(v,o,ct);
    public static Task<DeleteResult> ScopedDeleteOneAsync<T>(this IMongoCollection<T> c,Expression<Func<T,bool>> f,CancellationToken ct) =>
        TournamentEconomyRepository.Session is {} s ? c.DeleteOneAsync(s,f,cancellationToken:ct) : c.DeleteOneAsync(f,ct);
    public static Task<DeleteResult> ScopedDeleteManyAsync<T>(this IMongoCollection<T> c,Expression<Func<T,bool>> f,CancellationToken ct) =>
        TournamentEconomyRepository.Session is {} s ? c.DeleteManyAsync(s,f,cancellationToken:ct) : c.DeleteManyAsync(f,ct);
    public static Task<UpdateResult> ScopedUpdateOneAsync<T>(this IMongoCollection<T> c,FilterDefinition<T> f,UpdateDefinition<T> u,UpdateOptions? o,CancellationToken ct) =>
        TournamentEconomyRepository.Session is {} s ? c.UpdateOneAsync(s,f,u,o,ct) : c.UpdateOneAsync(f,u,o,ct);
    public static Task<ReplaceOneResult> ScopedReplaceOneAsync<T>(this IMongoCollection<T> c,Expression<Func<T,bool>> f,T v,ReplaceOptions o,CancellationToken ct) =>
        TournamentEconomyRepository.Session is {} s ? c.ReplaceOneAsync(s,f,v,o,ct) : c.ReplaceOneAsync(f,v,o,ct);
}
