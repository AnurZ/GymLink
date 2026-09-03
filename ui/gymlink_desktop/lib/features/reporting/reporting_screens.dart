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

const statisticsPalette = <Color>[
  Color(0xFF2864E8),
  Color(0xFF0F9D8A),
  Color(0xFFF59E0B),
  Color(0xFF7C3AED),
  Color(0xFFE85D75),
  Color(0xFF16A34A),
];

final class NiceAxisScale {
  const NiceAxisScale({required this.interval, required this.maximum});

  final double interval;
  final double maximum;
}

NiceAxisScale reservationCountAxis(int maximumCount) {
  if (maximumCount <= 0) {
    return const NiceAxisScale(interval: 1, maximum: 4);
  }
  final rawInterval = maximumCount / 4;
  final magnitude = math
      .pow(10, (math.log(rawInterval) / math.ln10).floor())
      .toDouble();
  final normalized = rawInterval / magnitude;
  final niceMultiplier = normalized <= 1
      ? 1
      : normalized <= 2
      ? 2
      : normalized <= 5
      ? 5
      : 10;
  final interval = math.max(1.0, niceMultiplier * magnitude).toDouble();
  var maximum = (maximumCount / interval).ceil() * interval;
  if (maximum <= maximumCount) maximum += interval;
  return NiceAxisScale(interval: interval, maximum: maximum);
}

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

  Future<void> _refreshAll() =>
      Future.wait([_loadSummary(), _loadMonths(), _loadDistribution()]);

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
        builder: (dialogContext) => GymLinkDialog(
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
    final refreshing =
        _summaryLoading || _monthsLoading || _distributionLoading;
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
            OutlinedButton.icon(
              key: const Key('refresh-gym-statistics'),
              onPressed: refreshing ? null : _refreshAll,
              icon: refreshing
                  ? const SizedBox.square(
                      dimension: 16,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(Icons.refresh),
              label: const Text('Osvježi'),
            ),
            const SizedBox(width: 12),
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
          subtitle: _reportingPeriod(_trends?['window']),
          loading: _trendsLoading,
          error: _trendsError,
          onRetry: _loadTrends,
          child: _MonthlyBarChart(
            data: _trends == null
                ? null
                : {'items': _trends!['reservationsByMonth']},
            reservationCountStyle: true,
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
    final membershipPeriodCount = value['membershipPeriodCount'] ?? 0;
    final change =
        (value['membershipPeriodChangePercentage'] as num?)?.toDouble() ?? 0;
    return Wrap(
      spacing: 16,
      runSpacing: 16,
      children: [
        _MetricCard(
          title: 'Broj aktivnih članova',
          value: '${value['activeMemberCount'] ?? 0}',
          detail:
              'Periodi članstva: $membershipPeriodCount '
              '(${change >= 0 ? '+' : ''}${change.toStringAsFixed(1)}% '
              'prema kraju prethodnog mjeseca)',
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
  const _MonthlyBarChart({
    required this.data,
    this.reservationCountStyle = false,
  });
  final Map<String, dynamic>? data;
  final bool reservationCountStyle;

  @override
  Widget build(BuildContext context) {
    final items = _items(data?['items']);
    if (items.isEmpty ||
        items.every((item) => (item['count'] as num? ?? 0) == 0)) {
      return const EmptyState('Nema podataka za posljednjih šest mjeseci.');
    }
    final maximumCount = items
        .map((item) => (item['count'] as num? ?? 0).toInt())
        .reduce(math.max);
    final scale = reservationCountAxis(maximumCount);
    return Padding(
      padding: const EdgeInsets.fromLTRB(8, 18, 12, 4),
      child: BarChart(
        BarChartData(
          maxY: scale.maximum,
          borderData: FlBorderData(show: false),
          gridData: FlGridData(
            show: true,
            drawVerticalLine: false,
            horizontalInterval: scale.interval,
            getDrawingHorizontalLine: (_) => FlLine(
              color: Theme.of(context).dividerColor.withValues(alpha: .42),
              strokeWidth: 1,
            ),
          ),
          barTouchData: BarTouchData(
            enabled: true,
            touchTooltipData: BarTouchTooltipData(
              fitInsideHorizontally: true,
              fitInsideVertically: true,
              tooltipPadding: reservationCountStyle
                  ? const EdgeInsets.symmetric(horizontal: 4, vertical: 2)
                  : null,
              tooltipMargin: reservationCountStyle ? 4 : null,
              getTooltipColor: reservationCountStyle
                  ? (_) => Colors.transparent
                  : null,
              getTooltipItem: reservationCountStyle
                  ? (group, groupIndex, rod, rodIndex) => BarTooltipItem(
                      rod.toY.toInt().toString(),
                      TextStyle(
                        color: Theme.of(context).colorScheme.onSurface,
                        fontWeight: FontWeight.w700,
                        fontSize: 12,
                      ),
                    )
                  : (group, groupIndex, rod, rodIndex) => BarTooltipItem(
                      '${rod.toY.toInt()} '
                      '${rod.toY.toInt() == 1 ? 'član' : 'članova'}',
                      TextStyle(
                        color: Theme.of(context).colorScheme.onInverseSurface,
                        fontWeight: FontWeight.w700,
                        fontSize: 12,
                      ),
                    ),
            ),
          ),
          titlesData: FlTitlesData(
            topTitles: const AxisTitles(
              sideTitles: SideTitles(showTitles: false),
            ),
            rightTitles: const AxisTitles(
              sideTitles: SideTitles(showTitles: false),
            ),
            leftTitles: AxisTitles(
              axisNameSize: reservationCountStyle ? 24 : 0,
              axisNameWidget: reservationCountStyle
                  ? const Text(
                      'Broj rezervacija',
                      key: Key('reservations-axis-title'),
                      style: TextStyle(fontSize: 12),
                    )
                  : null,
              sideTitles: SideTitles(
                showTitles: true,
                reservedSize: 42,
                interval: scale.interval,
                getTitlesWidget: (value, meta) => SideTitleWidget(
                  meta: meta,
                  child: Text(value.toInt().toString()),
                ),
              ),
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
                showingTooltipIndicators:
                    reservationCountStyle &&
                        (items[i]['count'] as num).toInt() > 0
                    ? const [0]
                    : const [],
                barRods: [
                  BarChartRodData(
                    toY: (items[i]['count'] as num).toDouble(),
                    width: 34,
                    color: reservationCountStyle
                        ? statisticsPalette.first
                        : statisticsPalette[i % statisticsPalette.length],
                    borderRadius: const BorderRadius.vertical(
                      top: Radius.circular(8),
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
                    color: statisticsPalette[i % statisticsPalette.length],
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
                Icon(
                  Icons.circle,
                  size: 11,
                  color: statisticsPalette[i % statisticsPalette.length],
                ),
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
    this.subtitle,
  });
  final String title;
  final bool loading;
  final Object? error;
  final VoidCallback onRetry;
  final Widget child;
  final String? subtitle;

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
          if (subtitle != null) ...[
            const SizedBox(height: 2),
            Text(
              subtitle!,
              key: const Key('statistics-reporting-window'),
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ],
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

String? _reportingPeriod(Object? value) {
  if (value is! Map) return null;
  final start = DateTime.tryParse(value['windowStart']?.toString() ?? '');
  final end = DateTime.tryParse(value['windowEnd']?.toString() ?? '');
  if (start == null || end == null) return null;
  String format(DateTime date) =>
      '${date.day.toString().padLeft(2, '0')}.'
      '${date.month.toString().padLeft(2, '0')}.${date.year}.';
  return 'Period: ${format(start)} – ${format(end)}';
}

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
