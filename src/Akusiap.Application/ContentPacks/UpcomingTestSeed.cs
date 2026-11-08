using Akusiap.Domain.Content;

namespace Akusiap.Application.ContentPacks;

/// <summary>
/// The starting list of upcoming tests (CPNS-06, D-129), researched on 2026-11-09. Only the CPNS 2026 registration month
/// is official; every other date is an estimate from the previous cycle and is marked Estimated with the reason in the
/// note. Admins keep the list current at <c>/admin/jadwal-tes</c>.
/// </summary>
public static class UpcomingTestSeed
{
    public const string CpnsSlug = "cpns-2026";

    public const string CpnsOutline =
        "Format SKD\n"
        + "110 soal pilihan ganda dalam 100 menit dengan komputer (CAT BKN), sekitar 54 detik per soal\n"
        + "TWK dan TIU: benar 5 poin, salah atau kosong 0\n"
        + "TKP: setiap pilihan bernilai 1 sampai 5\n"
        + "Ambang batas terakhir (2024): TWK 65, TIU 80, TKP 166, dan ketiganya harus tercapai\n"
        + "\n"
        + "Tes Wawasan Kebangsaan (TWK), 30 soal\n"
        + "Nasionalisme\n"
        + "Integritas\n"
        + "Bela negara\n"
        + "Pilar negara: Pancasila, UUD 1945, NKRI, Bhinneka Tunggal Ika\n"
        + "Bahasa negara\n"
        + "\n"
        + "Tes Intelegensia Umum (TIU), 35 soal\n"
        + "Verbal: analogi, silogisme, analitis\n"
        + "Numerik: berhitung, deret angka, perbandingan kuantitatif, soal cerita\n"
        + "Figural: analogi, ketidaksamaan, serial gambar\n"
        + "\n"
        + "Tes Karakteristik Pribadi (TKP), 45 soal\n"
        + "Pelayanan publik\n"
        + "Jejaring kerja\n"
        + "Sosial budaya\n"
        + "Teknologi informasi dan komunikasi\n"
        + "Profesionalisme\n"
        + "Anti radikalisme\n"
        + "\n"
        + "Setelah SKD\n"
        + "Peserta yang lolos ambang batas dan masuk peringkat formasi lanjut ke SKB sesuai jabatan yang dilamar";

    /// <summary>Fresh entities each call (the caller sets times and publishes them).</summary>
    public static IReadOnlyList<UpcomingTest> All() =>
    [
        new()
        {
            Slug = CpnsSlug,
            Name = "CPNS 2026",
            Organizer = "BKN dan KemenPAN-RB",
            Category = UpcomingTestCategory.Cpns,
            Summary = "Seleksi calon pegawai negeri sipil. Tahap pertama SKD: TWK, TIU dan TKP, 110 soal dalam 100 menit.",
            TestStartsOn = new DateOnly(2027, 1, 18),
            ScheduleStatus = ScheduleStatus.Estimated,
            ScheduleNote = "Pemerintah merencanakan pendaftaran dibuka November 2026 (Mensesneg, 6 November 2026). Tanggal SKD belum diumumkan; Januari 2027 adalah perkiraan dari siklus 2024, ketika SKD mulai sekitar 8 minggu setelah pendaftaran dibuka.",
            Outline = CpnsOutline,
            SourceUrl = "https://www.detik.com/kalimantan/berita/d-8742677/cpns-2026-dibuka-november-ini-tahapan-seleksi-dan-durasi-tes/amp",
        },
        new()
        {
            Slug = "pppk-2026",
            Name = "PPPK 2026",
            Organizer = "BKN dan KemenPAN-RB",
            Category = UpcomingTestCategory.Pppk,
            Summary = "Seleksi pegawai pemerintah dengan perjanjian kerja, lewat portal SSCASN.",
            ScheduleStatus = ScheduleStatus.NotAnnounced,
            ScheduleNote = "Belum ada jadwal resmi. Pada 6 November 2026 pemerintah menyampaikan PPPK paruh waktu akan diangkat menjadi PPPK penuh waktu.",
            Outline = "Seleksi kompetensi (CAT)\nKompetensi teknis sesuai jabatan\nKompetensi manajerial\nKompetensi sosial kultural\nWawancara",
            SourceUrl = "https://radartulungagung.jawapos.com/pendidikan/2611080024/pendaftaran-cpns-2026-direncanakan-november-pppk-paruh-waktu-naik-jadi-penuh-waktu",
        },
        new()
        {
            Slug = "polri-2027",
            Name = "Penerimaan Polri 2027",
            Organizer = "Kepolisian Negara Republik Indonesia",
            Category = UpcomingTestCategory.TniPolri,
            Summary = "Akpol, Bintara dan Tamtama, lewat penerimaan.polri.go.id.",
            RegistrationOpensOn = new DateOnly(2027, 3, 8),
            ScheduleStatus = ScheduleStatus.Estimated,
            ScheduleNote = "Jadwal 2027 belum diumumkan. Perkiraan Maret 2027, karena pendaftaran 2026 berlangsung 9 sampai 30 Maret 2026.",
            Outline = "Tahapan\nPemeriksaan administrasi\nKesehatan\nPsikologi\nAkademik\nJasmani",
            SourceUrl = "https://www.detik.com/jogja/berita/d-8417298/penerimaan-polri-2026-sampai-kapan-cek-jadwal-syarat-dan-daftar-bintaranya",
        },
        new()
        {
            Slug = "bumn-2027",
            Name = "Rekrutmen Bersama BUMN 2027",
            Organizer = "Forum Human Capital Indonesia (FHCI)",
            Category = UpcomingTestCategory.Bumn,
            Summary = "Satu pendaftaran untuk banyak BUMN, lewat rekrutmenbersama.fhcibumn.id.",
            RegistrationOpensOn = new DateOnly(2027, 5, 17),
            ScheduleStatus = ScheduleStatus.Estimated,
            ScheduleNote = "Jadwal 2027 belum diumumkan. Perkiraan Mei 2027, karena pembukaan 2026 diumumkan pertengahan Mei 2026.",
            Outline = "Tes daring\nTes kemampuan dasar: verbal, numerik, figural\nNilai inti AKHLAK BUMN\nBahasa Inggris\n\nTahap lanjutan\nTes bidang dan wawancara oleh tiap BUMN",
            SourceUrl = "https://exademy.com/jadwal-ujian/tes-kerja-bumn-rbb/",
        },
        new()
        {
            Slug = "pcpm-bi-42",
            Name = "PCPM Bank Indonesia Angkatan 42",
            Organizer = "Bank Indonesia",
            Category = UpcomingTestCategory.Banking,
            Summary = "Pendidikan Calon Pegawai Muda untuk lulusan S1 dan S2.",
            RegistrationOpensOn = new DateOnly(2027, 8, 9),
            ScheduleStatus = ScheduleStatus.Estimated,
            ScheduleNote = "Jadwal angkatan 42 belum diumumkan. Perkiraan Agustus 2027, karena angkatan 41 dibuka 9 sampai 14 Agustus 2026.",
            Outline = "Tahapan\nSeleksi administrasi\nPotensi dasar\nPengetahuan teknis\nPsikologi\nKesehatan\nWawancara",
            SourceUrl = "https://finance.detik.com/lainnya/d-8610226/pcpm-bank-indonesia-dibuka-hari-ini-cek-syaratnya",
        },
        new()
        {
            Slug = "kedinasan-2027",
            Name = "Sekolah Kedinasan 2027",
            Organizer = "BKN dan kementerian penyelenggara",
            Category = UpcomingTestCategory.Kedinasan,
            Summary = "Masuk sekolah ikatan dinas lewat SSCASN. Tahap pertamanya SKD, sama dengan CPNS.",
            TestStartsOn = new DateOnly(2027, 9, 20),
            ScheduleStatus = ScheduleStatus.Estimated,
            ScheduleNote = "Jadwal 2027 belum diumumkan. Perkiraan SKD September 2027, karena SKD sekolah kedinasan 2026 berlangsung 22 September sampai 7 Oktober 2026.",
            Outline = "SKD\nTWK, TIU dan TKP, seperti SKD CPNS\n\nTahap lanjutan\nTes kesehatan, kesamaptaan dan wawancara sesuai sekolah",
            SourceUrl = "https://www.detik.com/edu/detikpedia/d-8639378/pendaftaran-sekolah-kedinasan-tutup-besok-30-agustus-daftar-di-sini",
        },
    ];
}
