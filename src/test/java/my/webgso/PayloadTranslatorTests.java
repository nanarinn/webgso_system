package my.webgso;

import static org.assertj.core.api.Assertions.*;

import com.fasterxml.jackson.databind.ObjectMapper;
import java.time.*;
import org.junit.jupiter.api.Test;

class PayloadTranslatorTests {
  final PayloadTranslator parser = new PayloadTranslator(new ObjectMapper());
  final LocalDateTime submission = LocalDateTime.of(2026, 10, 22, 8, 0);

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
