import 'package:app_links/app_links.dart';
import 'package:url_launcher/url_launcher.dart';

import 'api.dart';

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
