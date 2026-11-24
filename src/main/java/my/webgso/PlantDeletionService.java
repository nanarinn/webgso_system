package my.webgso;

import my.webgso.Models.LssPlant;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
public class PlantDeletionService {
  private final Store db;
  private final Accounts accounts;

  public PlantDeletionService(Store db, Accounts accounts) {
    this.db = db;
    this.accounts = accounts;
  }

  @Transactional
  public String delete(int id, String actor) {
    var locked =
        db.jdbc().queryForList("SELECT \"Id\" FROM \"LssPlants\" WHERE \"Id\"=? FOR UPDATE", id);
    if (locked.isEmpty()) throw new IllegalArgumentException("Plant not found.");
    LssPlant plant = db.find(LssPlant.class, id).orElseThrow();
    long history =
        db.jdbc()
            .queryForObject(
                "SELECT (SELECT COUNT(*) FROM \"CollectionJobs\" WHERE \"PlantId\"=?) + (SELECT"
                    + " COUNT(*) FROM \"SolarApiReadings\" WHERE \"PlantId\"=?)",
                Long.class,
                id,
                id);
    if (history > 0)
      throw new IllegalArgumentException(
          "This plant has collection history and cannot be deleted. Set its status to Inactive"
              + " instead.");
    db.jdbc().update("DELETE FROM \"PlantApiConfigurations\" WHERE \"PlantId\"=?", id);
    db.jdbc().update("DELETE FROM \"CollectionSchedules\" WHERE \"PlantId\"=?", id);
    db.jdbc().update("DELETE FROM \"LssPlants\" WHERE \"Id\"=?", id);
    accounts.audit(actor, "Delete plant", "LssPlant", plant.plantId, "Success", plant.name);
    return plant.name;
  }

  /** Deletes a plant and its collected data for the isolated single-page viewer database. */
  @Transactional
  public String deleteViewerPlant(int id, String actor) {
    var locked =
        db.jdbc().queryForList("SELECT \"Id\" FROM \"LssPlants\" WHERE \"Id\"=? FOR UPDATE", id);
    if (locked.isEmpty()) throw new IllegalArgumentException("Plant not found.");
    LssPlant plant = db.find(LssPlant.class, id).orElseThrow();
    db.jdbc()
        .update(
            "DELETE FROM \"DataAnomalies\" WHERE \"CollectionJobId\" IN (SELECT \"Id\" FROM"
                + " \"CollectionJobs\" WHERE \"PlantId\"=?)",
            id);
    db.jdbc()
        .update(
            "DELETE FROM \"RawApiResponses\" WHERE \"CollectionJobId\" IN (SELECT \"Id\" FROM"
                + " \"CollectionJobs\" WHERE \"PlantId\"=?)",
            id);
    db.jdbc().update("DELETE FROM \"SolarApiReadings\" WHERE \"PlantId\"=?", id);
    db.jdbc().update("DELETE FROM \"CollectionJobs\" WHERE \"PlantId\"=?", id);
    db.jdbc().update("DELETE FROM \"CollectionSchedules\" WHERE \"PlantId\"=?", id);
    db.jdbc().update("DELETE FROM \"PlantApiConfigurations\" WHERE \"PlantId\"=?", id);
    db.jdbc().update("DELETE FROM \"LssPlants\" WHERE \"Id\"=?", id);
    accounts.audit(actor, "Delete viewer plant", "LssPlant", plant.plantId, "Success", plant.name);
    return plant.name;
  }
}
