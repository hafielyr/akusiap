using Akusiap.Application.Abstractions;
using Akusiap.Application.Questions.Import;
using ClosedXML.Excel;

namespace Akusiap.Infrastructure.Importing;

/// <summary>The xlsx template: a "Soal" sheet with the headers and three example rows, and a "Petunjuk" sheet.</summary>
internal sealed class QuestionTemplate : IQuestionTemplate
{
    private static readonly string[][] Examples =
    [
        ["SingleChoice", "Ibu kota Indonesia adalah …", "Jakarta", "Bandung", "Surabaya", "Medan", "", "", "", "", "A", "Jakarta adalah ibu kota negara.", "Ibu kota: Jakarta", "", "Subjek / Topik", "2", "Contoh"],
        ["MultiSelect", "Manakah yang termasuk bilangan prima?", "2", "4", "5", "9", "", "", "", "", "A,C", "2 dan 5 hanya habis dibagi 1 dan dirinya sendiri.", "", "Ingat definisi bilangan prima.", "Topik", "3", ""],
        ["TrueFalse", "Air mendidih pada 100 °C di permukaan laut.", "", "", "", "", "", "", "", "", "Benar", "Pada tekanan 1 atm air mendidih pada 100 °C.", "", "", "Topik", "1", ""],
    ];

    private static readonly string[][] Help =
    [
        ["Kolom", "Isi"],
        ["type", "SingleChoice (pilihan ganda), MultiSelect (pilihan ganda kompleks) atau TrueFalse (Benar/Salah). Wajib."],
        ["text", "Teks soal. Wajib."],
        ["option_a … option_h", "Pilihan jawaban berurutan tanpa celah: pilihan ganda 2–6, kompleks 2–8. Untuk TrueFalse boleh dikosongkan (Benar, Salah)."],
        ["correct", "Huruf jawaban benar, misalnya A atau A,C. Untuk TrueFalse: A/Benar atau B/Salah. Wajib."],
        ["explanation", "Pembahasan. Wajib."],
        ["key_fact", "Fakta kunci satu baris (maks. 200 karakter). Opsional."],
        ["hint", "Petunjuk. Opsional."],
        ["topic", "Nama topik yang sudah ada, atau \"Subjek / Topik\" bila nama topik dipakai di beberapa subjek. Wajib."],
        ["difficulty", "1–5. Opsional."],
        ["source_note", "Catatan sumber. Opsional."],
        ["", "Baris contoh di sheet Soal boleh dihapus. Jika ada satu baris yang salah, tidak ada soal yang disimpan."],
    ];

    public byte[] Create()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Soal");
        var columns = QuestionImportColumns.All;
        for (var c = 0; c < columns.Count; c++)
        {
            sheet.Cell(1, c + 1).Value = columns[c];
        }

        sheet.Row(1).Style.Font.Bold = true;
        for (var r = 0; r < Examples.Length; r++)
        {
            for (var c = 0; c < Examples[r].Length; c++)
            {
                sheet.Cell(r + 2, c + 1).Value = Examples[r][c];
            }
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, 4, 10, 60);

        var help = workbook.AddWorksheet("Petunjuk");
        for (var r = 0; r < Help.Length; r++)
        {
            help.Cell(r + 1, 1).Value = Help[r][0];
            help.Cell(r + 1, 2).Value = Help[r][1];
        }

        help.Row(1).Style.Font.Bold = true;
        help.Column(1).Width = 22;
        help.Column(2).Width = 100;
        help.Column(2).Style.Alignment.WrapText = true;

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }
}
