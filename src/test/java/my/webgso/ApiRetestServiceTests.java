package my.webgso;

import static org.assertj.core.api.Assertions.*;
import static org.mockito.Mockito.*;

import java.time.*;
import java.util.*;
import java.util.concurrent.*;
import my.webgso.Models.LssPlant;
import org.junit.jupiter.api.*;

class ApiRetestServiceTests {
  Store db = mock(Store.class);
  ApiTestHistoryService history = mock(ApiTestHistoryService.class);
  CollectionService collection = mock(CollectionService.class);

  private static ApiTestHistoryService.Entry saved(long id, int plantId, String dataset) {
    return new ApiTestHistoryService.Entry(
        id,
        plantId,
        dataset,
        OffsetDateTime.now(),
        "admin",
        "Success",
        200,
        5L,
        10L,
        "Previous result");
  }

  private static CollectionService.ApiTestResult success() {
    return new CollectionService.ApiTestResult(
        new PlantApiClient.Response(200, "application/json", "{}", 10), 1, 5, 0, 1);
  }

  private static void complete(ApiRetestService.Job job) throws InterruptedException {
    while (!job.complete) Thread.sleep(5);
  }

  @Test
  @Timeout(5)
  void rechecksEachPreviouslyTestedApiOnceAndContinuesAfterFailure() throws Exception {
    var plant = new LssPlant();
    plant.id = 1;
    plant.name = "Plant A";
    when(db.list(LssPlant.class, "")).thenReturn(List.of(plant));
    when(history.latest())
        .thenReturn(
            Map.of(
                "1:Day Ahead",
                saved(1, 1, "Day Ahead"),
                "1:MMF",
                saved(2, 1, "MMF"),
                "deleted",
                saved(3, 999, "MMF")));
    when(collection.retestWithAnomalies(1, "Day Ahead", "admin"))
        .thenThrow(
            new PlantApiClient.Failure(
                "HTTP 503", new PlantApiClient.Response(503, "text/plain", "unavailable", 15)));
    when(collection.retestWithAnomalies(1, "MMF", "admin")).thenReturn(success());
    var service = new ApiRetestService(db, history, collection);
    try {
      var job = service.start("owner", "admin");
      complete(job);
      assertThat(job.items)
          .extracting(ApiRetestService.Item::status)
          .containsExactly("Failed", "Success");
      assertThat(job.items.getFirst().httpStatus()).isEqualTo(503);
      verify(collection, times(1)).retestWithAnomalies(1, "Day Ahead", "admin");
      verify(collection, times(1)).retestWithAnomalies(1, "MMF", "admin");
      verifyNoMoreInteractions(collection);
    } finally {
      service.close();
    }
  }

  @Test
  @Timeout(5)
  void duplicateClicksReuseRunningJobAndOtherAccountsCannotReadIt() throws Exception {
    var plant = new LssPlant();
    plant.id = 1;
    plant.name = "Plant A";
    when(db.list(LssPlant.class, "")).thenReturn(List.of(plant));
    when(history.latest()).thenReturn(Map.of("saved", saved(1, 1, "MMF")));
    var entered = new CountDownLatch(1);
    var release = new CountDownLatch(1);
    when(collection.retestWithAnomalies(1, "MMF", "admin"))
        .thenAnswer(
            call -> {
              entered.countDown();
              release.await(3, TimeUnit.SECONDS);
              return success();
            });
    var service = new ApiRetestService(db, history, collection);
    try {
      var job = service.start("owner", "admin");
      assertThat(entered.await(2, TimeUnit.SECONDS)).isTrue();
      assertThat(service.start("owner", "admin")).isSameAs(job);
      assertThatThrownBy(() -> service.start("other-owner", "other-admin"))
          .isInstanceOf(IllegalArgumentException.class);
      assertThatThrownBy(() -> service.get(job.id, "other-owner"))
          .isInstanceOf(org.springframework.security.access.AccessDeniedException.class);
      release.countDown();
      complete(job);
      verify(collection, times(1)).retestWithAnomalies(1, "MMF", "admin");
    } finally {
      release.countDown();
      service.close();
    }
  }
}
