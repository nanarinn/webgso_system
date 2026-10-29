package my.webgso;

import java.time.*;
import java.util.*;
import java.util.concurrent.*;
import my.webgso.Models.*;
import org.springframework.security.access.AccessDeniedException;
import org.springframework.stereotype.Service;

@Service
public class ManualBatchService implements AutoCloseable {
  public record Item(
      int plantId, String plantName, String status, String message, Long collectionJobId) {}

  public static class Batch {
    public final String id = UUID.randomUUID().toString();
    public final String owner;
    public final Instant createdAt = Instant.now();
    public final ConcurrentMap<Integer, Item> items = new ConcurrentHashMap<>();

    public Batch(String owner) {
      this.owner = owner;
    }

    public boolean complete() {
      return items.values().stream().noneMatch(i -> Set.of("Queued", "Running").contains(i.status));
    }
  }

  private final Store db;
  private final CollectionService collection;
  private final ConcurrentMap<String, Batch> batches = new ConcurrentHashMap<>();
  private final ExecutorService executor =
      new ThreadPoolExecutor(
          6,
          6,
          0,
          TimeUnit.SECONDS,
          new ArrayBlockingQueue<>(256),
          new ThreadPoolExecutor.AbortPolicy());

  public ManualBatchService(Store db, CollectionService collection) {
    this.db = db;
    this.collection = collection;
  }

  public synchronized Batch start(
      Set<Integer> selected,
      String type,
      LocalDateTime submission,
      boolean replace,
      String owner,
      String actor) {
    batches
        .values()
        .removeIf(b -> b.complete() && Duration.between(b.createdAt, Instant.now()).toHours() > 1);
    if (batches.values().stream().anyMatch(b -> b.owner.equals(owner) && !b.complete()))
      throw new IllegalArgumentException("Your previous batch is still running.");
    var links = db.list(PlantApiConfiguration.class, "");
    String dataset = Policies.canonical(type);
    var plants =
        db.list(LssPlant.class, "WHERE \"IsActive\"=true ORDER BY \"PlantId\"").stream()
            .filter(p -> selected.isEmpty() || selected.contains(p.id))
            .filter(p -> ConfiguredDatasets.forPlant(p, links).contains(dataset))
            .toList();
    if (plants.isEmpty() || plants.size() > 100)
      throw new IllegalArgumentException(
          "Choose 1–100 active plants with a saved API URL for this dataset");
    if (submission.isAfter(LocalDateTime.now(Policies.LOCAL).plusMinutes(1)))
      throw new IllegalArgumentException("Submission cannot be in the future");
    var batch = new Batch(owner);
    for (var p : plants) batch.items.put(p.id, new Item(p.id, p.name, "Queued", "Waiting", null));
    batches.put(batch.id, batch);
    for (var p : plants)
      try {
        executor.submit(
            () -> {
              batch.items.put(p.id, new Item(p.id, p.name, "Running", "Fetching API", null));
              try {
                var job =
                    collection.collect(
                        p.id,
                        type,
                        submission,
                        replace,
                        actor,
                        null,
                        "BATCH-" + batch.id.substring(0, 12) + "-" + p.id,
                        0);
                batch.items.put(
                    p.id,
                    new Item(
                        p.id,
                        p.name,
                        job.status,
                        job.errorMessage == null ? job.responseMessage : job.errorMessage,
                        job.id));
              } catch (Exception e) {
                batch.items.put(
                    p.id,
                    new Item(
                        p.id,
                        p.name,
                        "Failed",
                        Objects.toString(e.getMessage(), "Collection failed"),
                        null));
              }
            });
      } catch (RejectedExecutionException full) {
        batch.items.put(
            p.id,
            new Item(
                p.id,
                p.name,
                "Failed",
                "Collection queue is full. Try this plant again later.",
                null));
      }
    return batch;
  }

  public Batch get(String id, String owner) {
    var batch = batches.get(id);
    if (batch == null)
      throw new IllegalArgumentException(
          "Batch status expired. Use the persisted collection history.");
    if (!batch.owner.equals(owner))
      throw new AccessDeniedException("This batch belongs to another user");
    return batch;
  }

  @Override
  public void close() {
    executor.shutdown();
  }
}
