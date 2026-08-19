<?php

declare(strict_types=1);

namespace DropShield\Connector\Model;

use Magento\Framework\App\Request\Http as HttpRequest;

/**
 * Builds the route claim from the actual Commerce request received by Magento. Query strings
 * are intentionally outside Origin Assertion v1's existing route claim; the connector still
 * receives the original query unchanged and supported protected operations do not use it for
 * mutation semantics.
 */
final class OriginAssertionRequestRoute
{
    public static function fromRequest(HttpRequest $request): string
    {
        $path = (string) $request->getPathInfo();
        if ($path === '' || $path[0] !== '/') {
            throw new \RuntimeException('Commerce request path is unavailable for origin assertion validation.');
        }

        return strtoupper((string) $request->getMethod()) . ' ' . $path;
    }
}
