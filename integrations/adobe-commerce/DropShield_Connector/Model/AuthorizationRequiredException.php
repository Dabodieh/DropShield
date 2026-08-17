<?php

declare(strict_types=1);

namespace DropShield\Connector\Model;

use Magento\Framework\Exception\LocalizedException;

/**
 * Thrown when a protected drop mutation is missing a valid DropShield
 * origin assertion. Message is intentionally generic; no cryptographic
 * or configuration detail is ever included.
 */
class AuthorizationRequiredException extends LocalizedException
{
}
