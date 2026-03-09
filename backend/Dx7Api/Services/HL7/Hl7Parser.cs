namespace Dx7Api.Services.Hl7;

/// <summary>
/// Parses HL7 v2.x messages (ORM^O01 orders and ORU^R01 results)
/// from Sysmex/InstaHMS format used by LABExpress clinics.
/// </summary>
public static class Hl7Parser
{
    public static Hl7Message Parse(string rawMessage)
    {
        // Normalize line endings: \r, \r\n, \n all become \n
        var lines = rawMessage
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var msg = new Hl7Message();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var seg = line.Split('|');
            var segId = seg[0];

            switch (segId)
            {
                case "MSH": ParseMsh(seg, msg); break;
                case "PID": ParsePid(seg, msg); break;
                case "OBR": ParseObr(seg, msg); break;
                case "OBX": msg.Observations.Add(ParseObx(seg)); break;
            }
        }

        return msg;
    }

    private static void ParseMsh(string[] seg, Hl7Message msg)
    {
        msg.SendingApp      = GetField(seg, 3);
        msg.SendingFacility = GetField(seg, 4);
        msg.MessageType     = GetField(seg, 8);   // MSH-9: ORM^O01 or ORU^R01
        msg.MessageId       = GetField(seg, 9);   // MSH-10: message control ID
        msg.Timestamp       = ParseHl7DateTime(GetField(seg, 6));  // MSH-7
    }

    private static void ParsePid(string[] seg, Hl7Message msg)
    {
        // PID-3: patient ID - format: MR000006^InstaHMS
        var pid3 = GetField(seg, 3).Split('^');
        msg.PatientId      = pid3.Length > 0 ? pid3[0] : "";

        // PID-5: name - format: DIMACULANGAN^AIDA^^^Mrs.
        var name = GetField(seg, 5).Split('^');
        msg.PatientLastName  = name.Length > 0 ? name[0] : "";
        msg.PatientFirstName = name.Length > 1 ? name[1] : "";
        msg.PatientName      = $"{msg.PatientLastName}, {msg.PatientFirstName}".Trim().TrimEnd(',').Trim();

        msg.PatientDob    = GetField(seg, 7);   // YYYYMMDD
        msg.PatientGender = GetField(seg, 8);   // M or F
    }

    private static void ParseObr(string[] seg, Hl7Message msg)
    {
        // OBR-2: placer order number — format: 178..BLD000035^InstaHMS
        var obr2 = GetField(seg, 2).Split('^');
        msg.PlacerOrderNumber = obr2.Length > 0 ? obr2[0] : "";

        // Extract accession: 178..BLD000035 → BLD000035
        var parts = msg.PlacerOrderNumber.Split("..");
        msg.AccessionId = parts.Length > 1 ? parts[1] : msg.PlacerOrderNumber;

        // OBR-4: test code — format: DGC0074^CBC (COMPLETE BLOOD COUNT)^InstaHMS^^
        var obr4 = GetField(seg, 4).Split('^');
        msg.TestCode = obr4.Length > 0 ? obr4[0] : "";
        msg.TestName = obr4.Length > 1 ? obr4[1] : "";

        // OBR-7: observation date/time
        msg.ObservationDateTime = ParseHl7DateTime(GetField(seg, 7));
        if (msg.ObservationDateTime == default)
            msg.ObservationDateTime = msg.Timestamp;

        // OBR-18: source lab / specimen ID  
        msg.SourceLab = GetField(seg, 18);
    }

    private static Hl7Observation ParseObx(string[] seg)
    {
        // OBX-3: test identifier — format: code^name^system
        var obx3 = GetField(seg, 3).Split('^');
        // OBX-5: result value
        // OBX-6: units — format: unit^description^system
        var obx6 = GetField(seg, 6).Split('^');
        // OBX-7: reference range
        // OBX-8: abnormal flags (H/L/N)
        // OBX-14: date/time of observation

        return new Hl7Observation
        {
            TestCode       = obx3.Length > 0 ? obx3[0] : "",
            TestName       = obx3.Length > 1 ? obx3[1] : "",
            ResultValue    = GetField(seg, 5),
            ResultUnit     = obx6.Length > 0 ? obx6[0] : GetField(seg, 6),
            ReferenceRange = GetField(seg, 7),
            AbnormalFlag   = GetField(seg, 8).ToUpper() switch {
                "H" => "H", "HH" => "H", ">" => "H",
                "L" => "L", "LL" => "L", "<" => "L",
                "N" => "N", "" => "N",
                var x => x
            },
            ObservationDateTime = ParseHl7DateTime(GetField(seg, 14)),
            ResultStatus   = GetField(seg, 11), // F=Final, P=Preliminary, C=Corrected
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string GetField(string[] seg, int index)
        => index < seg.Length ? seg[index].Trim() : "";

    public static DateTime ParseHl7DateTime(string hl7dt)
    {
        if (string.IsNullOrEmpty(hl7dt)) return default;
        hl7dt = hl7dt.Split('+')[0].Split('-')[0]; // strip timezone
        return hl7dt.Length switch
        {
            >= 14 => DateTime.TryParseExact(hl7dt[..14], "yyyyMMddHHmmss", null,
                         System.Globalization.DateTimeStyles.None, out var dt14) ? dt14 : default,
            >= 12 => DateTime.TryParseExact(hl7dt[..12], "yyyyMMddHHmm", null,
                         System.Globalization.DateTimeStyles.None, out var dt12) ? dt12 : default,
            >= 8  => DateTime.TryParseExact(hl7dt[..8],  "yyyyMMdd", null,
                         System.Globalization.DateTimeStyles.None, out var dt8) ? dt8 : default,
            _     => default
        };
    }
}

// ── HL7 message models ────────────────────────────────────────────────────────

public class Hl7Message
{
    public string SendingApp          { get; set; } = "";
    public string SendingFacility     { get; set; } = "";
    public string MessageType         { get; set; } = "";  // ORM^O01 or ORU^R01
    public string MessageId           { get; set; } = "";
    public DateTime Timestamp         { get; set; }

    // Patient
    public string PatientId           { get; set; } = "";  // MR number
    public string PatientName         { get; set; } = "";
    public string PatientLastName     { get; set; } = "";
    public string PatientFirstName    { get; set; } = "";
    public string PatientDob          { get; set; } = "";
    public string PatientGender       { get; set; } = "";

    // Order
    public string PlacerOrderNumber   { get; set; } = "";
    public string AccessionId         { get; set; } = "";
    public string TestCode            { get; set; } = "";
    public string TestName            { get; set; } = "";
    public DateTime ObservationDateTime { get; set; }
    public string SourceLab           { get; set; } = "";

    // Results (OBX segments - present in ORU^R01)
    public List<Hl7Observation> Observations { get; set; } = new();

    public bool IsOrder  => MessageType.StartsWith("ORM");
    public bool IsResult => MessageType.StartsWith("ORU");
}

public class Hl7Observation
{
    public string TestCode            { get; set; } = "";
    public string TestName            { get; set; } = "";
    public string ResultValue         { get; set; } = "";
    public string ResultUnit          { get; set; } = "";
    public string ReferenceRange      { get; set; } = "";
    public string AbnormalFlag        { get; set; } = "N";
    public string ResultStatus        { get; set; } = "F";
    public DateTime ObservationDateTime { get; set; }
}