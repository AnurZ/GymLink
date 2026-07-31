import 'package:app_links/app_links.dart';
import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';

import 'api.dart';

enum ReservationPaymentMethod { stripe, payInPerson }

Future<ReservationPaymentMethod?> chooseReservationPaymentMethod(
  BuildContext context,
) => showModalBottomSheet<ReservationPaymentMethod>(
  context: context,
  showDragHandle: true,
  builder: (context) => SafeArea(
    child: Padding(
      padding: const EdgeInsets.fromLTRB(20, 4, 20, 24),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Odaberite način plaćanja',
            style: Theme.of(
              context,
            ).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w800),
          ),
          const SizedBox(height: 12),
          Card(
            child: ListTile(
              key: const Key('reservation-payment-stripe'),
              leading: const Icon(Icons.credit_card),
              title: const Text('Stripe'),
              subtitle: const Text(
                'Otvorit će se vanjski preglednik. Nakon uspješnog '
                'plaćanja automatski ćete se vratiti u GymLink.',
              ),
              trailing: const Icon(Icons.chevron_right),
              onTap: () =>
                  Navigator.pop(context, ReservationPaymentMethod.stripe),
            ),
          ),
          Card(
            child: ListTile(
              key: const Key('reservation-payment-in-person'),
              leading: const Icon(Icons.payments_outlined),
              title: const Text('Plati uživo'),
              subtitle: const Text(
                'Termin se odmah potvrđuje, a iznos plaćate na terminu.',
              ),
              trailing: const Icon(Icons.chevron_right),
              onTap: () =>
                  Navigator.pop(context, ReservationPaymentMethod.payInPerson),
            ),
          ),
        ],
      ),
    ),
  ),
);

final class PaymentDeepLinks {
  final AppLinks _links = AppLinks();
  Uri? initialLink;

  Stream<Uri> get links => _links.uriLinkStream;

  Future<void> initialize() async {
    initialLink = await _links.getInitialLink();
  }

  static bool isPaymentResult(Uri uri) =>
      uri.scheme == 'gymlink' && uri.host == 'payment' && uri.path == '/result';
}

Future<String> openHostedCheckout(
  ApiClient api,
  String path, {
  Map<String, Object?>? body,
}) async {
  final response = Map<String, dynamic>.from(
    (await api.post(path, body: body))! as Map,
  );
  final checkoutUrl = Uri.tryParse(response['checkoutUrl']?.toString() ?? '');
  if (checkoutUrl == null ||
      !checkoutUrl.isScheme('https') ||
      !await launchUrl(checkoutUrl, mode: LaunchMode.externalApplication)) {
    throw StateError('Stripe plaćanje nije moguće otvoriti. Pokušajte ponovo.');
  }

  return response['paymentId'].toString();
}
