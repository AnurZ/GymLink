import 'dart:io';
import 'dart:math' as math;
import 'dart:typed_data';

import 'package:file_picker/file_picker.dart';
import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';
import 'package:printing/printing.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../shared/widgets.dart';

typedef SaveReport = Future<bool> Function(DownloadedFile report);
typedef PrintReport = Future<void> Function(DownloadedFile report);

class GymAdminReportsScreen extends StatefulWidget {
  const GymAdminReportsScreen({super.key, this.saveReport, this.printReport});

  final SaveReport? saveReport;
  final PrintReport? printReport;

  @override
  State<GymAdminReportsScreen> createState() => _GymAdminReportsScreenState();
}

class _GymAdminReportsScreenState extends State<GymAdminReportsScreen> {
  Map<String, dynamic>? _summary;
  Map<String, dynamic>? _months;
  Map<String, dynamic>? _distribution;
  Object? _summaryError;
  Object? _monthsError;
  Object? _distributionError;
  bool _summaryLoading = true;
  bool _monthsLoading = true;
  bool _distributionLoading = true;
  bool _exporting = false;

  @override
  void initState() {
    super.initState();
    _loadSummary();
    _loadMonths();
    _loadDistribution();
  }

  Future<void> _loadSummary() async {
    setState(() {
      _summaryLoading = true;
      _summaryError = null;
    });
    try {
      _summary = _map(
        await context.read<ApiClient>().get('/api/tenant/statistics/summary'),
      );
    } catch (error) {
      _summaryError = error;
    } finally {
      if (mounted) setState(() => _summaryLoading = false);
    }
  }

  Future<void> _loadMonths() async {
    setState(() {
      _monthsLoading = true;
      _monthsError = null;
    });
    try {
      _months = _map(
        await context.read<ApiClient>().get(
          '/api/tenant/statistics/members-by-month',
        ),
      );
    } catch (error) {
      _monthsError = error;
    } finally {
      if (mounted) setState(() => _monthsLoading = false);
    }
  }

  Future<void> _loadDistribution() async {
    setState(() {
      _distributionLoading = true;
      _distributionError = null;
    });
    try {
      _distribution = _map(
        await context.read<ApiClient>().get(
          '/api/tenant/statistics/membership-plan-distribution',
        ),
      );
    } catch (error) {
      _distributionError = error;
    } finally {
      if (mounted) setState(() => _distributionLoading = false);
    }
  }

  Future<void> _export(String type) async {
    if (_exporting) return;
    setState(() => _exporting = true);
    try {
      final report = await context.read<ApiClient>().download(
        type == 'memberships'
            ? '/api/tenant/reports/memberships.pdf'
            : '/api/tenant/reports/reservations.pdf',
      );
      if (!mounted) return;
      setState(() => _exporting = false);
      await showDialog<void>(
        context: context,
        builder: (dialogContext) => AlertDialog(
          title: const Text('PDF izvještaj je spreman'),
          content: Text(
            report.recordCount == 0
                ? 'Izvještaj nema zapisa u posljednjih šest mjeseci.'
                : 'Broj zapisa: ${report.recordCount}',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(dialogContext),
              child: const Text('Zatvori'),
            ),
            OutlinedButton.icon(
              onPressed: () => _save(report),
              icon: const Icon(Icons.save_alt),
              label: const Text('Sačuvaj'),
            ),
            FilledButton.icon(
              onPressed: () => _print(report),
              icon: const Icon(Icons.print_outlined),
              label: const Text('Štampaj'),
            ),
          ],
        ),
      );
    } catch (error) {
      if (mounted) {
        _message('Generisanje izvještaja nije uspjelo: $error', true);
      }
    } finally {
      if (mounted) setState(() => _exporting = false);
    }
  }

  Future<void> _save(DownloadedFile report) async {
    try {
      final saved = await (widget.saveReport ?? _saveDefault)(report);
      if (mounted) {
        _message(
          saved
              ? 'PDF izvještaj je sačuvan.'
              : 'Spremanje izvještaja je otkazano.',
          false,
        );
      }
    } catch (error) {
      if (mounted) _message('PDF nije moguće sačuvati: $error', true);
    }
  }

  Future<void> _print(DownloadedFile report) async {
    try {
      await (widget.printReport ?? _printDefault)(report);
      if (mounted) _message('Izvještaj je poslan dijalogu za štampu.', false);
    } catch (error) {
      if (mounted) _message('PDF nije moguće štampati: $error', true);
    }
  }

  void _message(String value, bool error) => ScaffoldMessenger.of(context)
    ..hideCurrentSnackBar()
    ..showSnackBar(
      SnackBar(
        content: Text(value),
        backgroundColor: error ? Theme.of(context).colorScheme.error : null,
      ),
    );

  @override
  Widget build(BuildContext context) {
    final chartWidth = math.max(
      360.0,
      (MediaQuery.sizeOf(context).width - 390) / 2,
    );
    return ListView(
      children: [
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'Izvještaji i statistika',
                    style: Theme.of(context).textTheme.headlineMedium?.copyWith(
                      fontWeight: FontWeight.w800,
                    ),
                  ),
                  const SizedBox(height: 4),
                  const Text(
                    'Pregled performansi teretane · posljednjih 6 mjeseci',
                  ),
                ],
              ),
            ),
            PopupMenuButton<String>(
              key: const Key('export-pdf-menu'),
              enabled: !_exporting,
              tooltip: 'Eksportuj PDF',
              onSelected: _export,
              itemBuilder: (_) => const [
                PopupMenuItem(
                  key: Key('export-memberships'),
                  value: 'memberships',
                  child: Text('Izvještaj o članstvima'),
                ),
                PopupMenuItem(
                  key: Key('export-reservations'),
                  value: 'reservations',
                  child: Text('Izvještaj o rezervacijama'),
                ),
              ],
              child: AnimatedContainer(
                key: const Key('export-pdf-trigger'),
                duration: const Duration(milliseconds: 150),
                constraints: const BoxConstraints(minHeight: 46),
                padding: const EdgeInsets.symmetric(horizontal: 18),
                decoration: BoxDecoration(
                  color: Theme.of(context).colorScheme.primary.withValues(
                    alpha: _exporting ? 0.58 : 1,
                  ),
                  borderRadius: BorderRadius.circular(10),
                ),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    if (_exporting)
                      SizedBox.square(
                        dimension: 18,
                        child: CircularProgressIndicator(
                          strokeWidth: 2,
                          color: Theme.of(context).colorScheme.onPrimary,
                        ),
                      )
                    else
                      Icon(
                        Icons.download_outlined,
                        color: Theme.of(context).colorScheme.onPrimary,
                      ),
                    const SizedBox(width: 8),
                    Text(
                      'Eksportuj PDF',
                      style: Theme.of(context).textTheme.labelLarge?.copyWith(
                        color: Theme.of(context).colorScheme.onPrimary,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: 24),
        _Section(
          loading: _summaryLoading,
          error: _summaryError,
          onRetry: _loadSummary,
          child: _SummaryCards(data: _summary),
        ),
        const SizedBox(height: 24),
        Wrap(
          spacing: 20,
          runSpacing: 20,
          children: [
            SizedBox(
              width: chartWidth,
              height: 390,
              child: _SectionCard(
                title: 'Broj članova po mjesecima',
                loading: _monthsLoading,
                error: _monthsError,
                onRetry: _loadMonths,
                child: _MonthlyBarChart(data: _months),
              ),
            ),
            SizedBox(
              width: chartWidth,
              height: 390,
              child: _SectionCard(
                title: 'Tipovi članstva',
                loading: _distributionLoading,
                error: _distributionError,
                onRetry: _loadDistribution,
                child: _PlanPieChart(data: _distribution),
              ),
            ),
          ],
        ),
      ],
    );
  }

  static Future<bool> _saveDefault(DownloadedFile report) async {
    final path = await FilePicker.platform.saveFile(
      dialogTitle: 'Sačuvaj PDF izvještaj',
      fileName: report.fileName,
      type: FileType.custom,
      allowedExtensions: const ['pdf'],
    );
    if (path == null) return false;
    await File(path).writeAsBytes(report.bytes, flush: true);
    return true;
  }

  static Future<void> _printDefault(DownloadedFile report) =>
      Printing.layoutPdf(
        name: report.fileName,
        onLayout: (_) async => Uint8List.fromList(report.bytes),
      );
}

class CentralStatisticsScreen extends StatefulWidget {
  const CentralStatisticsScreen({super.key});

  @override
  State<CentralStatisticsScreen> createState() =>
      _CentralStatisticsScreenState();
}

class _CentralStatisticsScreenState extends State<CentralStatisticsScreen> {
  Map<String, dynamic>? _summary;
  Map<String, dynamic>? _trends;
  Object? _summaryError;
  Object? _trendsError;
  bool _summaryLoading = true;
  bool _trendsLoading = true;

  @override
  void initState() {
    super.initState();
    _loadSummary();
    _loadTrends();
  }

  Future<void> _loadSummary() async {
    setState(() {
      _summaryLoading = true;
      _summaryError = null;
    });
    try {
      _summary = _map(
        await context.read<ApiClient>().get('/api/admin/statistics/summary'),
      );
    } catch (error) {
      _summaryError = error;
    } finally {
      if (mounted) setState(() => _summaryLoading = false);
    }
  }

  Future<void> _loadTrends() async {
    setState(() {
      _trendsLoading = true;
      _trendsError = null;
    });
    try {
      _trends = _map(
        await context.read<ApiClient>().get('/api/admin/statistics/trends'),
      );
    } catch (error) {
      _trendsError = error;
    } finally {
      if (mounted) setState(() => _trendsLoading = false);
    }
  }

  @override
  Widget build(BuildContext context) => ListView(
    children: [
      Text(
        'Statistika sistema',
        style: Theme.of(
          context,
        ).textTheme.headlineMedium?.copyWith(fontWeight: FontWeight.w800),
      ),
      const Text('Zbirni pregled za posljednjih 6 mjeseci'),
      const SizedBox(height: 24),
      _Section(
        loading: _summaryLoading,
        error: _summaryError,
        onRetry: _loadSummary,
        child: _SystemSummary(data: _summary),
      ),
      const SizedBox(height: 24),
      SizedBox(
        height: 390,
        child: _SectionCard(
          title: 'Rezervacije po mjesecima',
          loading: _trendsLoading,
          error: _trendsError,
          onRetry: _loadTrends,
          child: _MonthlyBarChart(
            data: _trends == null
                ? null
                : {'items': _trends!['reservationsByMonth']},
          ),
        ),
      ),
    ],
  );
}

class _SummaryCards extends StatelessWidget {
  const _SummaryCards({required this.data});
  final Map<String, dynamic>? data;

  @override
  Widget build(BuildContext context) {
    final value = data ?? const {};
    final change = (value['memberChangePercentage'] as num?)?.toDouble() ?? 0;
    return Wrap(
      spacing: 16,
      runSpacing: 16,
      children: [
        _MetricCard(
          title: 'Broj članova',
          value: '${value['activeMemberCount'] ?? 0}',
          detail:
              '${change >= 0 ? '+' : ''}${change.toStringAsFixed(1)}% ovaj mjesec',
          icon: Icons.people_outline,
          color: Colors.blue,
        ),
        _MetricCard(
          title: 'Broj rezervacija',
          value: '${value['reservationCount'] ?? 0}',
          detail: '+${value['reservationsToday'] ?? 0} danas',
          icon: Icons.calendar_month_outlined,
          color: Colors.green,
        ),
        _MetricCard(
          title: 'Prosječna ocjena',
          value: ((value['averageTrainerRating'] as num?)?.toDouble() ?? 0)
              .toStringAsFixed(1),
          detail: 'Ocjene trenera',
          icon: Icons.star_outline,
          color: Colors.amber,
        ),
      ],
    );
  }
}

class _SystemSummary extends StatelessWidget {
  const _SystemSummary({required this.data});
  final Map<String, dynamic>? data;

  @override
  Widget build(BuildContext context) {
    final value = data ?? const {};
    return Wrap(
      spacing: 16,
      runSpacing: 16,
      children: [
        _MetricCard(
          title: 'Teretane',
          value: '${value['totalGyms'] ?? 0}',
          detail: 'Ukupno',
          icon: Icons.apartment,
          color: Colors.blue,
        ),
        _MetricCard(
          title: 'Aktivni korisnici',
          value: '${value['activeUsers'] ?? 0}',
          detail: 'Aktivni računi',
          icon: Icons.people,
          color: Colors.green,
        ),
        _MetricCard(
          title: 'Rezervacije',
          value: '${value['reservationCount'] ?? 0}',
          detail: 'Posljednjih 6 mjeseci',
          icon: Icons.event,
          color: Colors.purple,
        ),
        _MetricCard(
          title: 'Čeka aktivaciju',
          value: '${value['pendingActivationGyms'] ?? 0}',
          detail: 'Teretane',
          icon: Icons.hourglass_top,
          color: Colors.orange,
        ),
      ],
    );
  }
}

class _MetricCard extends StatelessWidget {
  const _MetricCard({
    required this.title,
    required this.value,
    required this.detail,
    required this.icon,
    required this.color,
  });
  final String title;
  final String value;
  final String detail;
  final IconData icon;
  final Color color;

  @override
  Widget build(BuildContext context) => SizedBox(
    width: 260,
    child: Card(
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Row(
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title),
                  const SizedBox(height: 8),
                  Text(
                    value,
                    style: Theme.of(context).textTheme.headlineMedium?.copyWith(
                      fontWeight: FontWeight.w800,
                    ),
                  ),
                  const SizedBox(height: 6),
                  Text(detail, style: Theme.of(context).textTheme.bodySmall),
                ],
              ),
            ),
            DecoratedBox(
              decoration: BoxDecoration(
                color: color.withValues(alpha: .14),
                borderRadius: BorderRadius.circular(12),
              ),
              child: Padding(
                padding: const EdgeInsets.all(12),
                child: Icon(icon, color: color),
              ),
            ),
          ],
        ),
      ),
    ),
  );
}

class _MonthlyBarChart extends StatelessWidget {
  const _MonthlyBarChart({required this.data});
  final Map<String, dynamic>? data;

  @override
  Widget build(BuildContext context) {
    final items = _items(data?['items']);
    if (items.isEmpty ||
        items.every((item) => (item['count'] as num? ?? 0) == 0)) {
      return const EmptyState('Nema podataka za posljednjih šest mjeseci.');
    }
    return Padding(
      padding: const EdgeInsets.fromLTRB(8, 18, 12, 4),
      child: BarChart(
        BarChartData(
          maxY:
              items
                  .map((x) => (x['count'] as num).toDouble())
                  .reduce(math.max) *
              1.2,
          borderData: FlBorderData(show: false),
          gridData: const FlGridData(show: true, drawVerticalLine: false),
          barTouchData: BarTouchData(enabled: true),
          titlesData: FlTitlesData(
            topTitles: const AxisTitles(
              sideTitles: SideTitles(showTitles: false),
            ),
            rightTitles: const AxisTitles(
              sideTitles: SideTitles(showTitles: false),
            ),
            bottomTitles: AxisTitles(
              sideTitles: SideTitles(
                showTitles: true,
                reservedSize: 32,
                getTitlesWidget: (value, _) {
                  final index = value.toInt();
                  if (index < 0 || index >= items.length) {
                    return const SizedBox.shrink();
                  }
                  final month = (items[index]['month'] as num).toInt();
                  return Padding(
                    padding: const EdgeInsets.only(top: 8),
                    child: Text(_monthLabels[month - 1]),
                  );
                },
              ),
            ),
          ),
          barGroups: [
            for (var i = 0; i < items.length; i++)
              BarChartGroupData(
                x: i,
                barRods: [
                  BarChartRodData(
                    toY: (items[i]['count'] as num).toDouble(),
                    width: 34,
                    color: i == items.length - 1
                        ? Colors.blue.shade700
                        : Colors.blue.shade300,
                    borderRadius: const BorderRadius.vertical(
                      top: Radius.circular(4),
                    ),
                  ),
                ],
              ),
          ],
        ),
      ),
    );
  }
}

class _PlanPieChart extends StatelessWidget {
  const _PlanPieChart({required this.data});
  final Map<String, dynamic>? data;

  static const colors = [
    Colors.blue,
    Colors.lightBlue,
    Colors.indigo,
    Colors.teal,
    Colors.purple,
  ];

  @override
  Widget build(BuildContext context) {
    final items = _items(data?['items']);
    if (items.isEmpty) return const EmptyState('Nema aktivnih članstava.');
    return Column(
      children: [
        Expanded(
          child: PieChart(
            PieChartData(
              centerSpaceRadius: 58,
              sectionsSpace: 2,
              sections: [
                for (var i = 0; i < items.length; i++)
                  PieChartSectionData(
                    value: (items[i]['count'] as num).toDouble(),
                    color: colors[i % colors.length],
                    radius: 42,
                    showTitle: false,
                  ),
              ],
            ),
          ),
        ),
        for (var i = 0; i < items.length; i++)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 3),
            child: Row(
              children: [
                Icon(Icons.circle, size: 11, color: colors[i % colors.length]),
                const SizedBox(width: 8),
                Expanded(child: Text('${items[i]['planName']}')),
                Text('${items[i]['count']} (${items[i]['percentage']}%)'),
              ],
            ),
          ),
      ],
    );
  }
}

class _SectionCard extends StatelessWidget {
  const _SectionCard({
    required this.title,
    required this.loading,
    required this.error,
    required this.onRetry,
    required this.child,
  });
  final String title;
  final bool loading;
  final Object? error;
  final VoidCallback onRetry;
  final Widget child;

  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            title,
            style: Theme.of(
              context,
            ).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 8),
          Expanded(
            child: _Section(
              loading: loading,
              error: error,
              onRetry: onRetry,
              child: child,
            ),
          ),
        ],
      ),
    ),
  );
}

class _Section extends StatelessWidget {
  const _Section({
    required this.loading,
    required this.error,
    required this.onRetry,
    required this.child,
  });
  final bool loading;
  final Object? error;
  final VoidCallback onRetry;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    if (loading) return const Center(child: CircularProgressIndicator());
    if (error != null) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text('$error', textAlign: TextAlign.center),
            const SizedBox(height: 8),
            OutlinedButton(
              onPressed: onRetry,
              child: const Text('Pokušaj ponovo'),
            ),
          ],
        ),
      );
    }
    return child;
  }
}

Map<String, dynamic> _map(Object? value) =>
    Map<String, dynamic>.from(value! as Map);

List<Map<String, dynamic>> _items(Object? value) => (value as List? ?? const [])
    .whereType<Map>()
    .map((item) => Map<String, dynamic>.from(item))
    .toList(growable: false);

const _monthLabels = [
  'Jan',
  'Feb',
  'Mar',
  'Apr',
  'Maj',
  'Jun',
  'Jul',
  'Aug',
  'Sep',
  'Okt',
  'Nov',
  'Dec',
];
