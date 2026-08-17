<?php

declare(strict_types=1);

namespace DropShield\Connector\Model;

use Magento\Framework\App\RequestInterface;
use Magento\Framework\Phrase;
use Psr\Log\LoggerInterface;

/**
 * Enforces that the current request carries a valid DropShield origin
 * assertion for a protected drop. Reused by the cart and checkout plugins;
 * contains no cart/order-specific logic itself.
 */
class OriginAssertionGuard
{
    public function __construct(
        private readonly Config $config,
        private readonly OriginAssertionValidatorFactory $validatorFactory,
        private readonly LoggerInterface $logger
    ) {
    }

    /**
     * @throws AuthorizationRequiredException
     */
    public function requireValidAssertion(
        RequestInterface $request,
        string $drop,
        string $action,
        string $route
    ): void {
        if (!$this->config->isEnabled()) {
            return;
        }

        $headerName = $this->config->getHeaderName();
        $assertion = (string) $request->getHeader($headerName);
        if ($assertion === '') {
            throw new AuthorizationRequiredException(
                new Phrase('DropShield authorization required.')
            );
        }

        try {
            $validator = $this->validatorFactory->create();
        } catch (\RuntimeException $exception) {
            $this->logger->error('DropShield connector configuration is invalid.', ['exception' => $exception]);
            throw new AuthorizationRequiredException(
                new Phrase('DropShield authorization required.')
            );
        }

        $rawBody = (string) $request->getContent();
        $result = $validator->validate(
            $assertion,
            $drop,
            $action,
            (string) $request->getMethod(),
            $route,
            $rawBody
        );

        if (!$result->isValid()) {
            $this->logger->warning('DropShield connector rejected protected mutation.', [
                'reason' => $result->getFailureReason(),
                'route' => $route,
            ]);
            throw new AuthorizationRequiredException(
                new Phrase('DropShield authorization required.')
            );
        }
    }
}
