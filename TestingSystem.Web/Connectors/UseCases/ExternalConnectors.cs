namespace TestingSystem.Web.Backend.UseCases;

// FSD-DEFERRED(UCLSS020): AD/LDAPS authentication needs the organisation's directory
// host, certificate trust chain, bind strategy and group-to-role mapping. Keep local
// ASP.NET Core Identity enabled until those approved values are supplied.
public interface IActiveDirectoryAuthenticator
{
    Task<bool> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken);
}

// FSD-DEFERRED(UCLSS021-UCLSS024): production collection and validation require the
// confirmed plant API schemas, authentication method, thresholds and retry policy.
public interface IPlantDataCollectionPipeline
{
    Task<long> CollectAsync(int plantId, string dataType, DateOnly submissionDate,
        CancellationToken cancellationToken);
}

// FSD-DEFERRED(UCLSS025): scheduled delivery needs SMTP or Microsoft Graph settings,
// approved sender identity, recipient policy and network access.
public interface IScheduledReportDelivery
{
    Task DeliverAsync(long reportExecutionId, CancellationToken cancellationToken);
}

// FSD-DEFERRED(UCLSS026): the FSD requires a predefined substitution method but does
// not define the reference hierarchy or numeric rules. Implement only after business
// owners approve those rules; the service must never overwrite source observations.
public interface IDataSubstitutionPolicy
{
    Task<SubstitutionDecision> DecideAsync(long anomalyId, CancellationToken cancellationToken);
}

public sealed record SubstitutionDecision(bool CanSubstitute, decimal? Value,
    string? ReferenceSource, string Reason);
