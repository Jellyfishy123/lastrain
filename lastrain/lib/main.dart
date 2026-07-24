import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;

// API base URL. For local development it defaults to http://localhost:5000.
// For release/store builds, override it at build time with your Railway URL:
//   flutter build appbundle --dart-define=API_BASE_URL=https://your-app.up.railway.app
//   flutter build ipa       --dart-define=API_BASE_URL=https://your-app.up.railway.app
// (Store builds MUST use the https Railway URL — a phone can't reach localhost.)
const String apiBaseUrl = String.fromEnvironment(
  'API_BASE_URL',
  defaultValue: 'http://localhost:5000',
);

void main() => runApp(const LastTrainApp());

class LastTrainApp extends StatelessWidget {
  const LastTrainApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Last Train Home',
      theme: ThemeData(
        colorSchemeSeed: const Color(0xFFD42E12), // NSL red, why not
        useMaterial3: true,
      ),
      home: const PlannerPage(),
    );
  }
}

class LegResult {
  final String fromStation, toStation, lineName, towardsTerminus, latestDeparture;
  final bool isVerified;
  final double travelMinutes;
  LegResult.fromJson(Map<String, dynamic> j)
      : fromStation = j['fromStation'],
        toStation = j['toStation'],
        lineName = j['lineName'],
        towardsTerminus = j['towardsTerminus'],
        latestDeparture = j['latestDeparture'],
        isVerified = j['isVerified'],
        travelMinutes = (j['travelMinutes'] as num).toDouble();
}

class JourneyResult {
  final bool feasible;
  final String? reason;
  final String? latestDepartureFromOrigin;
  final List<LegResult> legs;
  final bool anyUnverifiedData;
  final bool alreadyDeparted;
  final int? minutesRemaining;
  final String? timingWarning;
  final double? totalTripMinutes;
  final String? estimatedArrival;
  JourneyResult.fromJson(Map<String, dynamic> j)
      : feasible = j['feasible'],
        reason = j['reason'],
        latestDepartureFromOrigin = j['latestDepartureFromOrigin'],
        anyUnverifiedData = j['anyUnverifiedData'] ?? false,
        alreadyDeparted = j['alreadyDeparted'] ?? false,
        minutesRemaining = j['minutesRemaining'],
        timingWarning = j['timingWarning'],
        totalTripMinutes = (j['totalTripMinutes'] as num?)?.toDouble(),
        estimatedArrival = j['estimatedArrival'],
        legs = (j['legs'] as List).map((e) => LegResult.fromJson(e)).toList();
}

class DayContext {
  final String date, dayOfWeek, dayType;
  final bool isPublicHoliday;
  DayContext.fromJson(Map<String, dynamic> j)
      : date = j['date'],
        dayOfWeek = j['dayOfWeek'],
        dayType = j['dayType'],
        isPublicHoliday = j['isPublicHoliday'] ?? false;
}

class StationCodeInfo {
  final String code, colourHex;
  StationCodeInfo({required this.code, required this.colourHex});
  factory StationCodeInfo.fromJson(Map<String, dynamic> j) =>
      StationCodeInfo(code: j['code'], colourHex: j['colourHex']);

  Color get color {
    final hex = colourHex.replaceFirst('#', '');
    return Color(int.parse('FF$hex', radix: 16));
  }
}

class StationInfo {
  final String name;
  final List<StationCodeInfo> codes;
  StationInfo({required this.name, required this.codes});
  factory StationInfo.fromJson(Map<String, dynamic> j) => StationInfo(
        name: j['name'],
        codes: (j['codes'] as List).map((e) => StationCodeInfo.fromJson(e)).toList(),
      );
}

/// Small colored badges showing a station's line codes, e.g. "CC10" in
/// Circle Line orange, "DT26" in Downtown Line blue — same codes/colours
/// as the official line legend. Renders nothing if the station isn't
/// recognized (e.g. still typing) or has no codes loaded yet.
class StationCodeBadges extends StatelessWidget {
  final String stationName;
  final Map<String, List<StationCodeInfo>> codesByName;
  final double fontSize;
  const StationCodeBadges({super.key, required this.stationName, required this.codesByName, this.fontSize = 10});

  @override
  Widget build(BuildContext context) {
    final codes = codesByName[stationName];
    if (codes == null || codes.isEmpty) return const SizedBox.shrink();
    return Wrap(
      spacing: 4,
      runSpacing: 2,
      children: codes
          .map((c) => Container(
                padding: const EdgeInsets.symmetric(horizontal: 5, vertical: 1),
                decoration: BoxDecoration(color: c.color, borderRadius: BorderRadius.circular(4)),
                child: Text(
                  c.code,
                  style: TextStyle(
                    color: Colors.white,
                    fontSize: fontSize,
                    fontWeight: FontWeight.bold,
                  ),
                ),
              ))
          .toList(),
    );
  }
}

class PlannerPage extends StatefulWidget {
  const PlannerPage({super.key});
  @override
  State<PlannerPage> createState() => _PlannerPageState();
}

class _PlannerPageState extends State<PlannerPage> {
  final _fromController = TextEditingController(text: 'Bishan');
  final _toController = TextEditingController(text: 'Expo');
  List<String> _allStations = [];
  Map<String, List<StationCodeInfo>> _codesByName = {};
  DayContext? _dayContext;
  JourneyResult? _result;
  String? _error;
  bool _loading = false;

  @override
  void initState() {
    super.initState();
    _loadStations();
    _loadDayContext();
  }

  Future<void> _loadDayContext() async {
    try {
      final res = await http.get(Uri.parse('$apiBaseUrl/api/daycontext'));
      if (res.statusCode == 200) {
        setState(() => _dayContext = DayContext.fromJson(jsonDecode(res.body)));
      }
    } catch (_) {
      // Not critical — the clock line still shows without it.
    }
  }

  Future<void> _loadStations() async {
    try {
      final res = await http.get(Uri.parse('$apiBaseUrl/api/stations'));
      if (res.statusCode == 200) {
        final list = (jsonDecode(res.body) as List).map((e) => StationInfo.fromJson(e)).toList();
        setState(() {
          _allStations = list.map((s) => s.name).toList();
          _codesByName = {for (final s in list) s.name: s.codes};
        });
      }
    } catch (_) {
      // Backend not reachable yet — the autocomplete just won't have suggestions.
    }
  }

  Future<void> _plan() async {
    setState(() {
      _loading = true;
      _error = null;
      _result = null;
    });
    try {
      final uri = Uri.parse('$apiBaseUrl/api/plan').replace(queryParameters: {
        'from': _fromController.text.trim(),
        'to': _toController.text.trim(),
      });
      final res = await http.get(uri);
      if (res.statusCode == 200) {
        setState(() => _result = JourneyResult.fromJson(jsonDecode(res.body)));
      } else {
        setState(() => _error = 'Server error (${res.statusCode})');
      }
    } catch (e) {
      setState(() => _error = 'Could not reach the API — is it running on $apiBaseUrl?');
    } finally {
      setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final now = TimeOfDay.now().format(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Last Train Home')),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 560),
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text('It\'s currently $now.', style: Theme.of(context).textTheme.bodyMedium),
                if (_dayContext != null) ...[
                  const SizedBox(height: 4),
                  Row(
                    children: [
                      Text(
                        '${_dayContext!.dayOfWeek}, ${_dayContext!.date} · ${_dayContext!.dayType}',
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                      if (_dayContext!.isPublicHoliday) ...[
                        const SizedBox(width: 6),
                        Container(
                          padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                          decoration: BoxDecoration(
                            color: Colors.purple.shade50,
                            borderRadius: BorderRadius.circular(4),
                          ),
                          child: Text('Public Holiday',
                              style: TextStyle(fontSize: 11, color: Colors.purple.shade800)),
                        ),
                      ],
                    ],
                  ),
                  const SizedBox(height: 2),
                  Text(
                    'Last-train times are the same every day, so this doesn\'t change your deadline below.',
                    style: TextStyle(fontSize: 11, color: Colors.grey.shade600, fontStyle: FontStyle.italic),
                  ),
                ],
                const SizedBox(height: 16),
                _StationField(controller: _fromController, label: 'From', options: _allStations, codesByName: _codesByName),
                const SizedBox(height: 12),
                _StationField(controller: _toController, label: 'To', options: _allStations, codesByName: _codesByName),
                const SizedBox(height: 20),
                FilledButton.icon(
                  onPressed: _loading ? null : _plan,
                  icon: const Icon(Icons.train),
                  label: Text(_loading ? 'Working it out…' : 'Find my last train'),
                ),
                const SizedBox(height: 24),
                if (_error != null)
                  Text(_error!, style: const TextStyle(color: Colors.red)),
                if (_result != null) _ResultView(result: _result!, codesByName: _codesByName),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _StationField extends StatelessWidget {
  final TextEditingController controller;
  final String label;
  final List<String> options;
  final Map<String, List<StationCodeInfo>> codesByName;
  const _StationField({
    required this.controller,
    required this.label,
    required this.options,
    required this.codesByName,
  });

  @override
  Widget build(BuildContext context) {
    if (options.isEmpty) {
      return TextField(
        controller: controller,
        decoration: InputDecoration(labelText: label, border: const OutlineInputBorder()),
      );
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Autocomplete<String>(
          initialValue: TextEditingValue(text: controller.text),
          optionsBuilder: (v) {
            if (v.text.isEmpty) return options; // show every station until they start narrowing it down
            return options.where((s) => s.toLowerCase().contains(v.text.toLowerCase()));
          },
          onSelected: (s) => controller.text = s,
          fieldViewBuilder: (context, textController, focusNode, onSubmit) {
            return TextField(
              controller: textController,
              focusNode: focusNode,
              decoration: InputDecoration(
                labelText: label,
                border: const OutlineInputBorder(),
                suffixIcon: IconButton(
                  icon: const Icon(Icons.arrow_drop_down),
                  onPressed: () {
                    textController.clear();
                    focusNode.requestFocus();
                  },
                ),
              ),
              onChanged: (v) => controller.text = v,
            );
          },
          optionsViewBuilder: (context, onSelected, opts) {
            final list = opts.toList();
            return Align(
              alignment: Alignment.topLeft,
              child: Material(
                elevation: 4,
                borderRadius: BorderRadius.circular(6),
                child: ConstrainedBox(
                  constraints: const BoxConstraints(maxHeight: 260, minWidth: 280),
                  child: ListView.builder(
                    padding: EdgeInsets.zero,
                    shrinkWrap: true,
                    itemCount: list.length,
                    itemBuilder: (context, i) {
                      final name = list[i];
                      return InkWell(
                        onTap: () => onSelected(name),
                        child: Padding(
                          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                          child: Row(
                            mainAxisAlignment: MainAxisAlignment.spaceBetween,
                            children: [
                              Flexible(child: Text(name, overflow: TextOverflow.ellipsis)),
                              const SizedBox(width: 8),
                              StationCodeBadges(stationName: name, codesByName: codesByName),
                            ],
                          ),
                        ),
                      );
                    },
                  ),
                ),
              ),
            );
          },
        ),
        // Live badges for whatever's currently in the field, so you can
        // confirm you've got the right station before hitting the button.
        AnimatedBuilder(
          animation: controller,
          builder: (context, _) => Padding(
            padding: const EdgeInsets.only(top: 4, left: 2),
            child: StationCodeBadges(stationName: controller.text, codesByName: codesByName),
          ),
        ),
      ],
    );
  }
}

class _ResultView extends StatelessWidget {
  final JourneyResult result;
  final Map<String, List<StationCodeInfo>> codesByName;
  const _ResultView({required this.result, required this.codesByName});

  @override
  Widget build(BuildContext context) {
    if (!result.feasible) {
      return Card(
        color: Colors.red.shade50,
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Text(result.reason ?? 'No route found.'),
        ),
      );
    }
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'Leave by ${result.latestDepartureFromOrigin}',
              style: Theme.of(context).textTheme.headlineSmall,
            ),
            const SizedBox(height: 4),
            const Text('to make every connection on this route.'),
            if (result.totalTripMinutes != null && result.estimatedArrival != null) ...[
              const SizedBox(height: 4),
              Text(
                'Trip takes ~${result.totalTripMinutes!.round()} min'
                '${result.legs.length > 1 ? ' (${result.legs.length - 1} transfer${result.legs.length > 2 ? 's' : ''})' : ''}'
                ' · arrive by ${result.estimatedArrival}',
                style: TextStyle(fontSize: 12, color: Colors.grey.shade700),
              ),
            ],
            if (result.timingWarning != null) ...[
              const SizedBox(height: 12),
              Container(
                padding: const EdgeInsets.all(8),
                decoration: BoxDecoration(
                  color: result.alreadyDeparted ? Colors.red.shade50 : Colors.orange.shade50,
                  borderRadius: BorderRadius.circular(6),
                ),
                child: Text(
                  (result.alreadyDeparted ? '✗ ' : '⏰ ') + result.timingWarning!,
                  style: TextStyle(
                    fontSize: 12,
                    fontWeight: FontWeight.bold,
                    color: result.alreadyDeparted ? Colors.red.shade900 : Colors.orange.shade900,
                  ),
                ),
              ),
            ],
            if (result.anyUnverifiedData) ...[
              const SizedBox(height: 12),
              Container(
                padding: const EdgeInsets.all(8),
                decoration: BoxDecoration(
                  color: Colors.amber.shade50,
                  borderRadius: BorderRadius.circular(6),
                ),
                child: const Text(
                  '⚠ One or more legs use placeholder timing data — '
                  'double-check those against sgtrains.com before relying on this.',
                  style: TextStyle(fontSize: 12),
                ),
              ),
            ],
            const Divider(height: 32),
            ...result.legs.map((leg) => Padding(
                  padding: const EdgeInsets.only(bottom: 12),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Icon(leg.isVerified ? Icons.check_circle : Icons.help_outline,
                          size: 18, color: leg.isVerified ? Colors.green : Colors.orange),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Wrap(
                              crossAxisAlignment: WrapCrossAlignment.center,
                              spacing: 6,
                              runSpacing: 2,
                              children: [
                                Text('${leg.fromStation}', style: const TextStyle(fontWeight: FontWeight.bold)),
                                StationCodeBadges(stationName: leg.fromStation, codesByName: codesByName),
                                const Text('→', style: TextStyle(fontWeight: FontWeight.bold)),
                                Text(leg.toStation, style: const TextStyle(fontWeight: FontWeight.bold)),
                                StationCodeBadges(stationName: leg.toStation, codesByName: codesByName),
                              ],
                            ),
                            const SizedBox(height: 2),
                            Wrap(
                              crossAxisAlignment: WrapCrossAlignment.center,
                              spacing: 6,
                              children: [
                                Text('${leg.lineName} · towards ${leg.towardsTerminus}'),
                                StationCodeBadges(stationName: leg.towardsTerminus, codesByName: codesByName, fontSize: 9),
                              ],
                            ),
                            Text('Latest departure: ${leg.latestDeparture}'),
                          ],
                        ),
                      ),
                    ],
                  ),
                )),
          ],
        ),
      ),
    );
  }
}
