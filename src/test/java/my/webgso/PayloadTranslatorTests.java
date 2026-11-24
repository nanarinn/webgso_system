package my.webgso;

import static org.assertj.core.api.Assertions.*;

import com.fasterxml.jackson.databind.ObjectMapper;
import java.time.*;
import org.junit.jupiter.api.Test;

class PayloadTranslatorTests {
  final PayloadTranslator parser = new PayloadTranslator(new ObjectMapper());
  final LocalDateTime submission = LocalDateTime.of(2026, 10, 22, 8, 0);

  @Test
  void rejectsInvalidStructureEvenWhenOtherRowsAreReadable() {
    for (String row :
        java.util.List.of(
            "{\"timestamp\":\"2026-10-22T08:00\",\"value\":\"bad\"}",
            "{\"timestamp\":\"2026-02-30T08:00\",\"value\":2}",
            "{\"value\":2}",
            "{\"timestamp\":\"2026-10-22T08:00\",\"parameter\":\"Power\"}",
            "{\"timestamp\":\"2026-10-22T08:00\",\"parameter\":\"\",\"value\":2}",
            "{\"timestamp\":\"2026-10-22T08:00\",\"endTime\":\"2026-10-22T07:00\",\"value\":2}",
            "{\"year\":2026,\"month\":2,\"day\":30,\"period\":1,\"value\":2}")) {
      assertThatThrownBy(
              () -> parse("[{\"timestamp\":\"2026-10-22T08:00\",\"value\":1}," + row + "]", "DDQ"))
          .isInstanceOf(PayloadTranslator.StructureFailure.class)
          .hasMessageContaining("API data structure is invalid");
    }
  }

  @Test
  void validatesMmfSensorsAndDeclaredTypeAndKeepsDatedNestedReadings() {
    assertThat(
            parse("{\"power\":{\"2026-10-22T08:00\":2,\"2026-10-22T08:15\":3}}", "DDQ").readings())
        .hasSize(2);
    assertThat(parse("{\"timestamp\":\"2026-10-22T08:00\",\"power\":[2,3]}", "DDQ").readings())
        .hasSize(2);
    var partialMmf =
        parse(
            "{\"timestamp\":\"2026-10-22T08:00\",\"parameter\":\"AmbientTemp\",\"value1\":20,\"value2\":\"NaN\"}",
            "MMF");
    assertThat(partialMmf.readings()).hasSize(1);
    assertThat(partialMmf.readings().getFirst().value).isEqualByComparingTo("20");
    assertThatThrownBy(
            () ->
                parse(
                    "{\"data_type\":\"MMF\",\"timestamp\":\"2026-10-22T08:00\",\"value\":2}",
                    "DDQ"))
        .hasMessageContaining("declares MMF");
    assertThat(
            parse(
                    "{\"timestamp\":\"2026-10-22T08:00\",\"readings\":[{\"parameter\":\"Power\",\"value\":2}]}",
                    "DDQ")
                .readings())
        .hasSize(1);
    assertThatThrownBy(
            () -> parse("timestamp,parameter,value\n2026-10-22T08:00,Power,bad\n", "DDQ"))
        .isInstanceOf(PayloadTranslator.StructureFailure.class);
    assertThatThrownBy(
            () ->
                parse("<rows><row><timestamp>bad</timestamp><value>1</value></row></rows>", "DDQ"))
        .isInstanceOf(PayloadTranslator.StructureFailure.class);
  }

  PayloadTranslator.Result parse(String payload, String api) {
    return parser.translate(
        payload, "application/octet-stream", api, "Day Ahead", 1, 2, submission, 15);
  }

  @Test
  void mmfSensorsAreAveragedAndExternalSubmissionCannotMoveReadings() {
    var result =
        parse(
            "{\"submissionDateTime\":\"2030-01-01T00:00\",\"readings\":[{\"timestamp\":\"2026-10-22T08:00\",\"parameter\":\"AmbientTemp\",\"value1\":20,\"value2\":30,\"value3\":0}]}",
            "MMF");
    assertThat(result.readings()).hasSize(1);
    assertThat(result.readings().getFirst().value).isEqualByComparingTo("16.666667");
    assertThat(result.readings().getFirst().submissionDateTime).isEqualTo(submission);
  }

  @Test
  void mmfKeepsValidRowsWhenAnotherMeasurementHasNoTimestamp() {
    var result =
        parse(
            "[{\"timestamp\":\"2026-10-22T08:00\",\"parameter\":\"Humidity\",\"value1\":70},"
                + "{\"parameter\":\"Humidity\",\"value1\":71}]",
            "MMF");
    assertThat(result.readings()).hasSize(1);
    assertThat(result.issues()).extracting(PayloadTranslator.Issue::code).contains("MMF-001");
  }

  @Test
  void acceptsLiveMmfSensorSnapshotAndUsesReceiptTimeWhenTimestampIsMissing() {
    var result =
        parse(
            "{\"MMF1\":{\"tgi\":393,\"amb_temp\":31.5,\"ws\":2,\"unused\":\"NaN\"},"
                + "\"MMF2\":{\"humid\":72,\"poa\":735}}",
            "MMF");

    assertThat(result.readings()).hasSize(5);
    assertThat(result.readings()).allMatch(reading -> reading.intervalStart.equals(submission));
    assertThat(result.readings())
        .extracting(reading -> reading.parameter)
        .containsExactlyInAnyOrder(
            "MMF1 · TGI",
            "MMF1 · Ambient Temperature",
            "MMF1 · Wind Speed",
            "MMF2 · Humidity",
            "MMF2 · POA Irradiance");
    assertThat(result.issues()).extracting(PayloadTranslator.Issue::code).contains("MMF-002");
  }

  @Test
  void acceptsColumnarMatrixAndCompositePeriods() {
    var columnar =
        parse(
            "{\"timestamp\":[\"2026-10-22T08:00\",\"2026-10-22T08:30\"],\"power\":[12,15]}", "DDQ");
    assertThat(columnar.readings()).hasSize(2);
    assertThat(columnar.readings().getFirst().intervalMinutes).isEqualTo(30);
    var matrix =
        parse(
            "{\"columns\":[\"year\",\"month\",\"day\",\"period\",\"value\"],\"data\":[[2026,10,22,2,7]]}",
            "DDQ");
    assertThat(matrix.readings()).hasSize(1);
    assertThat(matrix.readings().getFirst().intervalStart)
        .isEqualTo(LocalDateTime.of(2026, 10, 22, 0, 15));
  }

  @Test
  void acceptsSoapAndQuotedCsvAndRejectsExternalEntities() {
    var xml =
        parse(
            "<s:Envelope"
                + " xmlns:s='http://schemas.xmlsoap.org/soap/envelope/'><s:Body><row><timestamp>2026-10-22T08:00</timestamp><parameter>Power</parameter><value>8.5</value></row></s:Body></s:Envelope>",
            "DDQ");
    assertThat(xml.readings().getFirst().value).isEqualByComparingTo("8.5");
    assertThat(
            parse("timestamp,parameter,value\n2026-10-22T08:00,\"Forecast, MW\",12.5\n", "DDQ")
                .readings())
        .hasSize(1);
    assertThatThrownBy(
            () ->
                parse(
                    "<!DOCTYPE foo [<!ENTITY xxe SYSTEM"
                        + " 'file:///C:/Windows/win.ini'>]><r><value>&xxe;</value></r>",
                    "DDQ"))
        .isInstanceOf(IllegalArgumentException.class);
    assertThatThrownBy(() -> parse("<!doctype html><html>sign in</html>", "DDQ"))
        .isInstanceOf(IllegalArgumentException.class);
  }

  @Test
  void detectsDdqDuplicatesAndInfersSourceInterval() {
    var result =
        parse(
            "[{\"timestamp\":\"2026-10-22T08:00\",\"power\":5},{\"timestamp\":\"2026-10-22T08:00\",\"power\":7},{\"timestamp\":\"2026-10-22T09:00\",\"power\":8}]",
            "DDQ");
    assertThat(result.readings()).hasSize(2);
    assertThat(result.issues()).extracting(PayloadTranslator.Issue::code).contains("DDQ-002");
    assertThat(result.readings().getFirst().intervalMinutes).isEqualTo(60);
  }
}
