import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';

class CachedNetworkImageView extends StatelessWidget {
  const CachedNetworkImageView({
    required this.imageUrl,
    required this.fallback,
    this.fit = BoxFit.cover,
    this.width,
    this.height,
    this.decodeWidth = 512,
    this.decodeHeight = 512,
    super.key,
  });

  final String? imageUrl;
  final Widget fallback;
  final BoxFit fit;
  final double? width;
  final double? height;
  final int decodeWidth;
  final int decodeHeight;

  @override
  Widget build(BuildContext context) {
    final url = imageUrl;
    if (url == null || url.isEmpty) return fallback;
    return CachedNetworkImage(
      imageUrl: url,
      width: width,
      height: height,
      fit: fit,
      memCacheWidth: decodeWidth,
      memCacheHeight: decodeHeight,
      maxWidthDiskCache: decodeWidth,
      maxHeightDiskCache: decodeHeight,
      placeholder: (_, _) => Stack(
        fit: StackFit.expand,
        children: [
          fallback,
          Center(
            child: Icon(
              Icons.image_outlined,
              size: 20,
              color: Theme.of(context).colorScheme.outline,
            ),
          ),
        ],
      ),
      errorWidget: (_, _, _) => fallback,
    );
  }
}
