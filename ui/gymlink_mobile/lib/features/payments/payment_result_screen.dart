import 'dart:async';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';

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
  String? _message;

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
          setState(() {
            _paid = true;
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
    body: Center(
      child: Padding(
        padding: const EdgeInsets.all(28),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (_loading)
              const CircularProgressIndicator()
            else
              Icon(
                _paid ? Icons.check_circle : Icons.info_outline,
                size: 72,
                color: _paid ? Colors.green : Colors.blueGrey,
              ),
            const SizedBox(height: 18),
            Text(
              _loading ? 'Provjera plaćanja…' : (_paid ? 'Plaćeno' : _message!),
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.titleLarge,
            ),
            const SizedBox(height: 20),
            if (!_loading && !_paid)
              OutlinedButton(
                onPressed: _refresh,
                child: const Text('Osvježi status'),
              ),
            FilledButton(
              onPressed: () => context.go('/'),
              child: const Text('Nastavi'),
            ),
          ],
        ),
      ),
    ),
  );
}
