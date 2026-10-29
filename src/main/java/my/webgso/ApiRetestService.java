package my.webgso;

import jakarta.annotation.PreDestroy;
import java.util.*;
import java.util.concurrent.*;
import my.webgso.Models.LssPlant;
import my.webgso.Models.PlantApiConfiguration;
import org.springframework.security.access.AccessDeniedException;
import org.springframework.stereotype.Service;

@Service
public class ApiRetestService {
  public record Item(
      int plantId,
      String plantName,
      String dataset,
      String status,
      Integer httpStatus,
      Long responseMillis,
      String message) {}

  public static final class Job {
    public final String id = UUID.randomUUID().toString();
    public final String owner;
    public volatile List<Item> items;
    public volatile boolean complete;
    public boolean configured;

    Job(String owner, List<Item> items) {
      this.owner = owner;
      this.items = List.copyOf(items);
    }
  }

  private final Store db;
  private final ApiTestHistoryService history;
  private final CollectionService collection;
  private final LinkedHashMap<String, Job> jobs = new LinkedHashMap<>();
  private final ExecutorService executor = Executors.newSingleThreadExecutor();

  public ApiRetestService(Store db, ApiTestHistoryService history, CollectionService collection) {
    this.db = db;
    this.history = history;
    this.collection = collection;
  }

  public synchronized Job start(String owner, String actor) {
    return start(owner, actor, null, null);
  }

  public synchronized Job startConfigured(String owner, String actor) {
    for (var existing : jobs.values()) {
      if (!existing.complete) {
        if (existing.owner.equals(owner)) return existing;
        throw new IllegalArgumentException(
            "Another API check is already running. Please try again when it finishes.");
      }
    }
    var links = db.list(PlantApiConfiguration.class, "");
    var items = new ArrayList<Item>();
    for (var plant : db.list(LssPlant.class, "")) {
      for (String dataset : ConfiguredDatasets.forPlant(plant, links))
        items.add(new Item(plant.id, plant.name, dataset, "Queued", null, null, "Waiting"));
    }
    if (items.isEmpty())
      throw new IllegalArgumentException(
          "No APIs are configured. Add an API in Plant Config first.");
    while (jobs.size() >= 8) jobs.remove(jobs.keySet().iterator().next());
    var job = new Job(owner, items);
    job.configured = true;
    jobs.put(job.id, job);
    executor.execute(() -> run(job, actor));
    return job;
  }

  public synchronized Job startSingle(String owner, String actor, int plantId, String dataset) {
    var last = history.latest().get(ApiTestHistoryService.key(plantId, dataset));
    if (last == null) throw new IllegalArgumentException("This API has no saved test to recheck.");
    return start(owner, actor, plantId, dataset);
  }

  private Job start(String owner, String actor, Integer plantId, String dataset) {
    for (var existing : jobs.values())
      if (!existing.complete) {
        if (existing.owner.equals(owner)) return existing;
        throw new IllegalArgumentException(
            "Another API check is already running. Please try again when it finishes.");
      }
    while (jobs.size() >= 8) jobs.remove(jobs.keySet().iterator().next());
    Map<Integer, LssPlant> plants = new HashMap<>();
    db.list(LssPlant.class, "").forEach(plant -> plants.put(plant.id, plant));
    var items =
        history.latest().values().stream()
            .filter(test -> plants.containsKey(test.plantId()))
            .filter(
                test ->
                    plantId == null
                        || (test.plantId() == plantId && test.dataset().equals(dataset)))
            .sorted(
                Comparator.comparingInt(ApiTestHistoryService.Entry::plantId)
                    .thenComparing(ApiTestHistoryService.Entry::dataset))
            .map(
                test ->
                    new Item(
                        test.plantId(),
                        plants.get(test.plantId()).name,
                        test.dataset(),
                        "Queued",
                        null,
                        null,
                        "Waiting"))
            .toList();
    if (items.isEmpty())
      throw new IllegalArgumentException("There are no previously tested APIs to recheck.");
    var job = new Job(owner, items);
    jobs.put(job.id, job);
    executor.execute(() -> run(job, actor));
    return job;
  }

  public synchronized Job get(String id, String owner) {
    var job = jobs.get(id);
    if (job == null)
      throw new IllegalArgumentException(
          "API check not found. Open API test history to start a new check.");
    if (!job.owner.equals(owner))
      throw new AccessDeniedException("This API check belongs to another account.");
    return job;
  }

  private void run(Job job, String actor) {
    var results = new ArrayList<>(job.items);
    try {
      for (int i = 0; i < results.size(); i++) {
        var target = results.get(i);
        results.set(
            i,
            new Item(
                target.plantId(),
                target.plantName(),
                target.dataset(),
                "Running",
                null,
                null,
                "Checking API"));
        job.items = List.copyOf(results);
        Item result;
        try {
          var tested =
              job.configured
                      && !history
                          .latest()
                          .containsKey(
                              ApiTestHistoryService.key(target.plantId(), target.dataset()))
                  ? collection.testWithAnomalies(target.plantId(), target.dataset(), actor)
                  : collection.retestWithAnomalies(target.plantId(), target.dataset(), actor);
          var response = tested.response();
          result =
              new Item(
                  target.plantId(),
                  target.plantName(),
                  target.dataset(),
                  "Success",
                  response.status(),
                  response.elapsedMillis(),
                  tested.readings() + " readable measurements; " + tested.issues() + " anomalies");
        } catch (Exception e) {
          var response = e instanceof PlantApiClient.Failure failure ? failure.response() : null;
          result =
              new Item(
                  target.plantId(),
                  target.plantName(),
                  target.dataset(),
                  "Failed",
                  response == null ? null : response.status(),
                  response == null ? null : response.elapsedMillis(),
                  Objects.toString(e.getMessage(), "API check failed"));
        }
        results.set(i, result);
        job.items = List.copyOf(results);
      }
    } finally {
      job.complete = true;
    }
  }

  @PreDestroy
  void close() {
    executor.shutdownNow();
  }
}
