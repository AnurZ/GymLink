import 'dart:async';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../chat/chat_screens.dart';
import '../reservations/reservation_refresh_controller.dart';

class PaymentResultScreen extends StatefulWidget {
  const PaymentResultScreen({
    required this.outcome,
    required this.paymentId,
    super.key,
  });

  final String? outcome;
  final String? paymentId;

  @override
  State<PaymentResultScreen> createState() => _PaymentResultScreenState();
}

class _PaymentResultScreenState extends State<PaymentResultScreen> {
  bool _loading = true;
  bool _paid = false;
  Map<String, dynamic>? _payment;
  String? _message;

  bool get _isPaidTrainerReservation =>
      _paid &&
      (_payment?['purpose'] as num?)?.toInt() == 1 &&
      _payment?['targetId'] != null;

  @override
  void initState() {
    super.initState();
    _refresh();
  }

  Future<void> _refresh() async {
    if (widget.outcome == 'cancel') {
      setState(() {
        _loading = false;
        _message = 'Plaćanje je prekinuto. Možete pokušati ponovo.';
      });
      return;
    }
    if (widget.paymentId == null || widget.paymentId!.isEmpty) {
      setState(() {
        _loading = false;
        _message = 'Status plaćanja nije moguće provjeriti.';
      });
      return;
    }

    setState(() {
      _loading = true;
      _message = null;
    });
    final api = context.read<ApiClient>();
    try {
      for (var attempt = 0; attempt < 5; attempt++) {
        final payment = Map<String, dynamic>.from(
          (await api.get('/api/payments/${widget.paymentId}'))! as Map,
        );
        if (payment['isPaid'] == true) {
          if (!mounted) return;
          if ((payment['purpose'] as num?)?.toInt() == 1) {
            context.read<ReservationRefreshController>().refresh();
          }
          setState(() {
            _paid = true;
            _payment = payment;
            _loading = false;
          });
          return;
        }
        if (attempt < 4) {
          await Future<void>.delayed(const Duration(seconds: 1));
        }
      }
      if (mounted) {
        setState(() {
          _loading = false;
          _message =
              'Potvrda još nije stigla. Osvježite status za nekoliko trenutaka.';
        });
      }
    } on ApiProblem catch (error) {
      if (mounted) {
        setState(() {
          _loading = false;
          _message = error.message;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Status plaćanja')),
    body: SafeArea(
      child: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(20),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 420),
            child: Card(
              margin: EdgeInsets.zero,
              child: Padding(
                padding: const EdgeInsets.fromLTRB(24, 28, 24, 24),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    if (_loading)
                      const Center(child: CircularProgressIndicator())
                    else
                      Icon(
                        _paid ? Icons.check_circle : Icons.info_outline,
                        size: 72,
                        color: _paid ? Colors.green : Colors.blueGrey,
                      ),
                    const SizedBox(height: 18),
                    Text(
                      _loading
                          ? 'Provjera plaćanja…'
                          : (_paid ? 'Plaćanje uspješno' : _message!),
                      textAlign: TextAlign.center,
                      style: Theme.of(context).textTheme.headlineSmall
                          ?.copyWith(fontWeight: FontWeight.w800),
                    ),
                    if (_isPaidTrainerReservation) ...[
                      const SizedBox(height: 8),
                      Text(
                        'Termin je potvrđen i razgovor s trenerom je spreman.',
                        textAlign: TextAlign.center,
                        style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                          color: Theme.of(context).colorScheme.onSurfaceVariant,
                        ),
                      ),
                    ],
                    const SizedBox(height: 28),
                    if (!_loading && !_paid)
                      SizedBox(
                        height: 52,
                        child: FilledButton.icon(
                          onPressed: _refresh,
                          icon: const Icon(Icons.refresh),
                          label: const Text('Osvježi status'),
                        ),
                      ),
                    if (_isPaidTrainerReservation)
                      SizedBox(
                        height: 52,
                        child: FilledButton.icon(
                          key: const Key('payment-open-chat'),
                          onPressed: () => openChatForReservation(
                            context,
                            _payment!['targetId'].toString(),
                          ),
                          icon: const Icon(Icons.forum_outlined),
                          label: const Text('Otvori razgovor'),
                        ),
                      ),
                    if (!_loading) ...[
                      if (!_paid || _isPaidTrainerReservation)
                        const SizedBox(height: 12),
                      SizedBox(
                        height: 52,
                        child: _isPaidTrainerReservation || !_paid
                            ? OutlinedButton.icon(
                                key: const Key('payment-return-home'),
                                onPressed: () => context.go('/'),
                                icon: const Icon(Icons.home_outlined),
                                label: const Text('Vrati se na početnu'),
                              )
                            : FilledButton.icon(
                                key: const Key('payment-return-home'),
                                onPressed: () => context.go('/'),
                                icon: const Icon(Icons.arrow_forward),
                                label: const Text('Nastavi'),
                              ),
                      ),
                    ],
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    ),
  );
}
