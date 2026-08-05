import 'package:app_links/app_links.dart';
import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';

import 'api.dart';
import 'theme.dart';

enum MembershipPaymentMethod { stripe, manual }

enum ReservationPaymentMethod { stripe, payInPerson, manual }

Future<MembershipPaymentMethod?> chooseMembershipPaymentMethod(
  BuildContext context,
) => showModalBottomSheet<MembershipPaymentMethod>(
  context: context,
  showDragHandle: true,
  builder: (context) => SafeArea(
    child: SingleChildScrollView(
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
          _stripePaymentTile(context, () {
            Navigator.pop(context, MembershipPaymentMethod.stripe);
          }),
          Card(
            child: ListTile(
              key: const Key('membership-payment-manual'),
              leading: const Icon(Icons.check_circle_outline),
              title: const Text('Označi kao plaćeno'),
              subtitle: const Text(
                'Testno plaćanje bez Stripe transakcije. Dostupno samo kada '
                'je ALLOW_FAKE_PAYMENTS uključen.',
              ),
              trailing: const Icon(Icons.chevron_right),
              onTap: () =>
                  Navigator.pop(context, MembershipPaymentMethod.manual),
            ),
          ),
        ],
      ),
    ),
  ),
);

Future<ReservationPaymentMethod?> chooseReservationPaymentMethod(
  BuildContext context,
) => showModalBottomSheet<ReservationPaymentMethod>(
  context: context,
  showDragHandle: true,
  builder: (context) => SafeArea(
    child: SingleChildScrollView(
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
          _stripePaymentTile(context, () {
            Navigator.pop(context, ReservationPaymentMethod.stripe);
          }, key: const Key('reservation-payment-stripe')),
          Card(
            child: ListTile(
              key: const Key('reservation-payment-manual'),
              leading: const Icon(Icons.check_circle_outline),
              title: const Text('Označi kao plaćeno'),
              subtitle: const Text(
                'Testno plaćanje bez Stripe transakcije. Dostupno samo kada '
                'je ALLOW_FAKE_PAYMENTS uključen.',
              ),
              trailing: const Icon(Icons.chevron_right),
              onTap: () =>
                  Navigator.pop(context, ReservationPaymentMethod.manual),
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

Widget _stripePaymentTile(
  BuildContext context,
  VoidCallback onTap, {
  Key key = const Key('membership-payment-stripe'),
}) => Card(
  child: ListTile(
    key: key,
    leading: const Icon(Icons.credit_card),
    title: const Text('Stripe'),
    subtitle: const Text(
      'Otvorit će se vanjski preglednik. Nakon uspješnog plaćanja '
      'automatski ćete se vratiti u GymLink.',
    ),
    trailing: const Icon(Icons.chevron_right),
    onTap: onTap,
  ),
);

Future<void> showPayInPersonReservationSuccess(BuildContext context) =>
    showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (context) => AlertDialog(
        key: const Key('pay-in-person-success-dialog'),
        icon: Container(
          key: const Key('pay-in-person-success-check'),
          width: 68,
          height: 68,
          decoration: const BoxDecoration(
            color: Color(0xFFE8F7EC),
            shape: BoxShape.circle,
          ),
          child: const Icon(
            Icons.check_circle,
            size: 44,
            color: GymLinkColors.success,
          ),
        ),
        title: const Text('Termin je potvrđen', textAlign: TextAlign.center),
        content: const Text(
          'Rezervacija je sačuvana u Terminima, a razgovor s trenerom je '
          'spreman. Iznos plaćate uživo na treningu.',
          textAlign: TextAlign.center,
        ),
        actions: [
          SizedBox(
            width: double.infinity,
            child: FilledButton.icon(
              key: const Key('pay-in-person-success-done'),
              onPressed: () => Navigator.pop(context),
              icon: const Icon(Icons.calendar_today_outlined),
              label: const Text('U redu'),
            ),
          ),
        ],
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
